using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Threading;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._Oxyd.Framework.ViewCalc;

public sealed class ViewTickEvent : EntityEventArgs
{
    public required HashSet<EntityUid> seen;
}

/// <summary>
/// Handles Line-Of-Sight view perception calculations for ViewTickerComponent entities.
/// Uses a high-performance hybrid architecture:
/// 1. Fast Integer Grid DDA when viewer and viewee are on the same grid (95%+ of cases).
/// 2. Any-Hit Physics Raycast fallback for off-grid (deep space) or cross-grid entities.
/// 3. Work spreading across server ticks to eliminate 1-second hitching.
/// 4. Parallel execution via IParallelManager without .NET ThreadPool task overhead.
/// </summary>
public sealed partial class ViewCalcSystem : EntitySystem
{
    [Dependency] private RayCastSystem _raycaster = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _entlook = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IParallelManager _parallel = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<OccluderComponent> _occluderQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    private readonly QueryFilter _opaqueFilter = new()
    {
        Flags = QueryFlags.Static,
        LayerBits = (int)CollisionGroup.Opaque,
        MaskBits = (int)CollisionGroup.Opaque
    };

    private sealed class ActiveViewerData
    {
        public EntityUid Uid;
        public ViewTickerComponent Ticker = default!;
        public MapCoordinates Coordinates;
        public EntityUid? GridUid;
        public MapGridComponent? Grid;
        public readonly HashSet<Entity<ViewRelevantComponent>> Candidates = new(64);
        public readonly HashSet<EntityUid> Seen = new(64);
    }

    private readonly List<ActiveViewerData> _activeViewers = new(32);
    private ViewJob _viewJob;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _occluderQuery = GetEntityQuery<OccluderComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _viewJob = new ViewJob { System = this };
    }

    public override void Update(float frameTime)
    {
        var curTick = _timing.CurTick.Value;
        var curTime = _timing.CurTime;
        var targetInterval = Math.Max((ushort)1, _timing.TickRate);

        var activeCount = 0;
        var query = EntityQueryEnumerator<ViewTickerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var ticker, out var xform))
        {
            // Work spreading: partition viewers across targetInterval ticks (e.g. 30 ticks = 1 sec at 30Hz)
            if ((curTick + (uint)uid.Id) % targetInterval != 0)
                continue;

            var coord = _transform.GetMapCoordinates(uid, xform);
            var distSq = (coord.Position - ticker.lastTickPosition.Position).LengthSquared();

            // Stationary cache check
            if (coord.MapId == ticker.lastTickPosition.MapId &&
                distSq < 2f &&
                curTime - ticker.lastTickTime <= TimeSpan.FromSeconds(5))
            {
                // Fix: targeted event dispatch to uid so subscribers receive cached ticks
                RaiseLocalEvent(uid, new ViewTickEvent { seen = ticker.lastSeen });
                continue;
            }

            // Needs fresh LOS calculation: assign to active batch
            if (activeCount >= _activeViewers.Count)
                _activeViewers.Add(new ActiveViewerData());

            var active = _activeViewers[activeCount++];
            active.Uid = uid;
            active.Ticker = ticker;
            active.Coordinates = coord;
            active.GridUid = xform.GridUid;
            active.Grid = null;
            if (xform.GridUid != null)
                _gridQuery.TryComp(xform.GridUid.Value, out active.Grid);
        }

        if (activeCount == 0)
            return;

        // Phase 1: Parallel Compute (Read-only LOS calculation using dedicated IParallelManager job)
        _parallel.ProcessNow(_viewJob, activeCount);

        // Phase 2: Sequential Event Dispatch on Main Thread (Ensures thread-safe ECS event delivery)
        for (var i = 0; i < activeCount; i++)
        {
            var active = _activeViewers[i];
            var ticker = active.Ticker;

            ticker.lastTickTime = curTime;
            ticker.lastTickPosition = active.Coordinates;

            ticker.lastSeen.Clear();
            ticker.lastSeen.UnionWith(active.Seen);

            RaiseLocalEvent(active.Uid, new ViewTickEvent { seen = ticker.lastSeen });
        }
    }

    private void ProcessViewer(int index)
    {
        var data = _activeViewers[index];
        data.Candidates.Clear();
        data.Seen.Clear();

        _entlook.GetEntitiesInRange(data.Coordinates, data.Ticker.range, data.Candidates, LookupFlags.Approximate);

        foreach (var candidate in data.Candidates)
        {
            if (candidate.Owner == data.Uid)
                continue;

            var candXform = _xformQuery.GetComponent(candidate.Owner);
            var candMapPos = _transform.GetMapCoordinates(candidate.Owner, candXform);

            if (candMapPos.MapId != data.Coordinates.MapId)
                continue;

            if (HasLineOfSight(data, candidate.Owner, candXform, candMapPos))
            {
                data.Seen.Add(candidate.Owner);
            }
        }
    }

    private bool HasLineOfSight(
        ActiveViewerData viewer,
        EntityUid targetUid,
        TransformComponent targetXform,
        MapCoordinates targetMapPos)
    {
        // Fast Path: Both entities are on the same grid (95%+ cases in SS14)
        if (viewer.GridUid != null && viewer.GridUid == targetXform.GridUid && viewer.Grid != null)
        {
            var p0 = _mapSystem.CoordinatesToTile(viewer.GridUid.Value, viewer.Grid, viewer.Coordinates);
            var p1 = _mapSystem.CoordinatesToTile(viewer.GridUid.Value, viewer.Grid, targetMapPos);

            if (HasGridTileLOS(viewer.GridUid.Value, viewer.Grid, p0, p1))
                return true;

            return false;
        }

        // Fallback Path: Cross-grid or off-grid in deep space
        return HasRaycastLOS(viewer.Coordinates, targetMapPos, targetUid);
    }

    private bool HasGridTileLOS(EntityUid gridUid, MapGridComponent grid, Vector2i start, Vector2i end)
    {
        var x0 = start.X;
        var y0 = start.Y;
        var x1 = end.X;
        var y1 = end.Y;

        if (x0 == x1 && y0 == y1)
            return true;

        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        var curX = x0;
        var curY = y0;

        while (true)
        {
            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                curX += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                curY += sy;
            }

            // Reached destination tile
            if (curX == x1 && curY == y1)
                return true;

            // Check if current tile has an anchored opaque fixture/occluder
            var anchored = _mapSystem.GetAnchoredEntities(gridUid, grid, new Vector2i(curX, curY));
            while (anchored.MoveNext(out var uid))
            {
                if (IsOpaque(uid.Value))
                    return false;
            }
        }
    }

    private bool HasRaycastLOS(MapCoordinates from, MapCoordinates to, EntityUid targetUid)
    {
        var dir = to.Position - from.Position;
        if (dir.LengthSquared() < 1e-6f)
            return true;

        var res = _raycaster.CastRayClosest(from.MapId, from.Position, dir, _opaqueFilter);
        return !res.Hit || (res.Results.Count > 0 && res.Results[0].Entity == targetUid);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool IsOpaque(EntityUid uid)
    {
        if (_physicsQuery.TryComp(uid, out var phys) && (phys.CollisionLayer & (int)CollisionGroup.Opaque) != 0)
            return true;

        if (_occluderQuery.TryComp(uid, out var occ) && occ.Enabled)
            return true;

        return false;
    }

    /// <summary>
    /// Legacy/utility method for finding entities in view of an arbitrary point.
    /// Uses the same optimized hybrid line of sight pipeline.
    /// </summary>
    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range)
    {
        var keepers = new HashSet<EntityUid>();
        var candidates = new HashSet<Entity<ViewRelevantComponent>>();
        _entlook.GetEntitiesInRange(point, range, candidates, LookupFlags.Approximate);

        _mapSystem.TryFindGridAt(point, out var gridUid, out var grid);

        foreach (var ent in candidates)
        {
            var targetUid = ent.Owner;
            var targetXform = _xformQuery.GetComponent(targetUid);
            var targetPos = _transform.GetMapCoordinates(targetUid, targetXform);

            if (targetPos.MapId != point.MapId)
                continue;

            if (grid != null && gridUid == targetXform.GridUid)
            {
                var p0 = _mapSystem.CoordinatesToTile(gridUid, grid, point);
                var p1 = _mapSystem.CoordinatesToTile(gridUid, grid, targetPos);
                if (HasGridTileLOS(gridUid, grid, p0, p1))
                {
                    keepers.Add(targetUid);
                }
            }
            else
            {
                if (HasRaycastLOS(point, targetPos, targetUid))
                {
                    keepers.Add(targetUid);
                }
            }
        }

        return keepers;
    }

    private record struct ViewJob : IParallelRobustJob
    {
        public int BatchSize => 2;

        public ViewCalcSystem System;

        public void Execute(int index)
        {
            System.ProcessViewer(index);
        }
    }
}
