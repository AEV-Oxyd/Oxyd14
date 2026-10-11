using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Framework.ViewCalc;

public class ViewTickEvent : EntityEventArgs
{
    public required IReadOnlySet<EntityUid> seen;
}

/// <summary>
/// Finds the visible entities for each view ticker once per second. View results are
/// cached on the ticker: they are only recalculated when the ticker moved more than
/// <see cref="MovementThreshold"/> tiles or the cached result is older than
/// <see cref="StaleAfter"/>.
/// </summary>
public sealed partial class ViewCalcSystem : EntitySystem
{
    [Dependency] private RayCastSystem raycaster = default!;
    [Dependency] private TransformSystem transform = default!;
    [Dependency] private EntityLookupSystem entlook = default!;
    [Dependency] private IGameTiming timing = default!;
    private TimeSpan _nextTick;

    /// <summary>View-relevant entities that moved or lost the component since the last
    /// tick. A stationary ticker only recalculates when one of these moved inside its
    /// range — a global "something moved" epoch would force every ticker to recalc.
    /// Keyed per uid so a physics-tick worth of MoveEvents costs two points, not two
    /// points per event. Old position (the one that left range) is kept in
    /// <see cref="_viewOldPositions"/>; the new position is resolved once at Update time.
    /// </summary>
    private readonly HashSet<EntityUid> _viewDirtyEnts = new();
    private readonly Dictionary<EntityUid, MapCoordinates> _viewOldPositions = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ViewRelevantComponent, MoveEvent>(OnViewRelevantMoved);
        SubscribeLocalEvent<ViewRelevantComponent, ComponentShutdown>(OnViewRelevantGone);
    }

    private void OnViewRelevantMoved(Entity<ViewRelevantComponent> ent, ref MoveEvent args)
    {
        _viewDirtyEnts.Add(ent.Owner);
        // MoveEvents also fire during deletion where the old parent is already invalid.
        // Keep only the first old position seen this tick — it marks the range the
        // entity is vacating; the Update-time position covers what it is entering.
        if (args.OldPosition.IsValid(EntityManager) &&
            !_viewOldPositions.ContainsKey(ent.Owner))
        {
            _viewOldPositions[ent.Owner] = transform.ToMapCoordinates(args.OldPosition);
        }
    }

    private void OnViewRelevantGone(Entity<ViewRelevantComponent> ent, ref ComponentShutdown args)
    {
        // Covers component removal on a live entity too — without it the entity would
        // stay in cached views until the staleness timer. Position is captured here
        // because the transform may already be gone at shutdown.
        if (TryComp(ent.Owner, out TransformComponent? xform))
        {
            _viewDirtyEnts.Add(ent.Owner);
            var map = transform.GetMapCoordinates(ent.Owner, xform);
            _viewOldPositions[ent.Owner] = map;
        }
    }

    /// <summary>Squared movement distance (≈1.4 tiles) that forces a view recalculation.</summary>
    private const float MovementThresholdSquared = 2f;

    /// <summary>How long a cached view stays valid for a stationary ticker.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    /// <summary>Shared line-of-sight filter so callers never allocate one per raycast.</summary>
    public readonly QueryFilter Filter = new()
    {
        Flags = QueryFlags.Static,
        LayerBits = (int) CollisionGroup.Opaque,
        MaskBits = (int) CollisionGroup.Opaque,
    };

    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range)
    {
        return GetEntsInView(point, range, Filter);
    }

    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range, QueryFilter filter)
    {
        HashSet<EntityUid> keepers = new();
        if (range <= 0)
            return keepers;

        HashSet<Entity<ViewRelevantComponent>> result = new();
        entlook.GetEntitiesInRange(point, range, result, LookupFlags.Approximate);
        foreach (var ent in result)
        {
            if (InLineOfSight(point, ent.Owner, filter))
                keepers.Add(ent);
        }
        return keepers;
    }

    /// <summary>Checks visibility with the shared ray filter.</summary>
    public bool InLineOfSight(MapCoordinates origin, EntityUid target)
    {
        return InLineOfSight(origin, target, Filter);
    }

    /// <summary>Checks visibility with the caller's shared ray filter.</summary>
    public bool InLineOfSight(MapCoordinates origin, EntityUid target, QueryFilter filter)
    {
        var res = raycaster.CastRayClosest(origin.MapId,
            origin.Position,
            transform.GetWorldPosition(target) - origin.Position,
            filter);
        return !res.Hit || res.Results.First().Entity == target;
    }

    public override void Update(float frameTime)
    {
        if (timing.CurTime < _nextTick)
            return;
        _nextTick = timing.CurTime + TimeSpan.FromSeconds(1);

        // Resolve the dirty set once: two points per moved entity (vacated spot and
        // current spot) regardless of how many MoveEvents it raised this tick.
        List<(EntityUid Entity, MapId MapId, Vector2 Position)>? dirtyPoints = null;
        if (_viewDirtyEnts.Count > 0)
        {
            dirtyPoints = new List<(EntityUid, MapId, Vector2)>();
            foreach (var dirtyEnt in _viewDirtyEnts)
            {
                if (_viewOldPositions.TryGetValue(dirtyEnt, out var oldPos))
                    dirtyPoints.Add((dirtyEnt, oldPos.MapId, oldPos.Position));
                if (!TerminatingOrDeleted(dirtyEnt) && TryComp(dirtyEnt, out TransformComponent? dirtyXform))
                {
                    var cur = transform.GetMapCoordinates(dirtyEnt, dirtyXform);
                    dirtyPoints.Add((dirtyEnt, cur.MapId, cur.Position));
                }
            }
        }

        var tickers = EntityQueryEnumerator<ViewTickerComponent>();
        while (tickers.MoveNext(out var uid, out var comp))
        {
            // Raise every second, but only recalculate when the ticker moved or the
            // cached view went stale — line of sight raycasts are expensive.
            var position = transform.GetMapCoordinates(uid);
            var effectiveRange = Math.Max(comp.range, comp.auraRange);
            var stale = timing.CurTime - comp.lastTickTime >= StaleAfter;
            if (!stale && dirtyPoints is { } points)
            {
                // A relevant entity moved or vanished inside (or into) this ticker's range.
                // The ticker's own movement is handled by `moved` below — sanity mobs
                // carry both components and must not recalc themselves stale every tick.
                var origin = comp.lastTickPosition ?? position;
                var rangeSquared = effectiveRange * effectiveRange;
                foreach (var (entity, mapId, point) in points)
                {
                    if (entity == uid)
                        continue;
                    if (mapId == origin.MapId &&
                        (point - origin.Position).LengthSquared() <= rangeSquared)
                    {
                        stale = true;
                        break;
                    }
                }
            }

            var moved = comp.lastTickPosition is not { } lastPosition ||
                position.MapId != lastPosition.MapId ||
                (position.Position - lastPosition.Position).LengthSquared() > MovementThresholdSquared;
            if (stale || moved)
            {
                comp.lastTickTime = timing.CurTime;
                comp.lastTickPosition = position;
                comp.lastSeen = GetEntsInView(position, effectiveRange);
            }
            else
            {
                comp.lastSeen.RemoveWhere(e => TerminatingOrDeleted(e));
            }
            RaiseLocalEvent(uid, new ViewTickEvent { seen = comp.lastSeen });
        }
        _viewDirtyEnts.Clear();
        _viewOldPositions.Clear();
    }
}
