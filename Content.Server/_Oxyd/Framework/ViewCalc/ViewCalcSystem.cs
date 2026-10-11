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

    /// <summary>Map positions where a view-relevant entity moved or disappeared since the
    /// last tick. A stationary ticker only recalculates when one of these falls inside its
    /// range — a global "something moved" epoch would force every ticker to recalc.
    /// </summary>
    private readonly List<(MapId MapId, Vector2 Position)> _viewDirty = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ViewRelevantComponent, MoveEvent>(OnViewRelevantMoved);
        SubscribeLocalEvent<ViewRelevantComponent, ComponentShutdown>(OnViewRelevantGone);
    }

    private void OnViewRelevantMoved(Entity<ViewRelevantComponent> ent, ref MoveEvent args)
    {
        // MoveEvents also fire during deletion where the old parent is already invalid.
        if (args.OldPosition.IsValid(EntityManager))
        {
            var oldMap = transform.ToMapCoordinates(args.OldPosition);
            _viewDirty.Add((oldMap.MapId, oldMap.Position));
        }
        if (args.NewPosition.IsValid(EntityManager))
        {
            var newMap = transform.ToMapCoordinates(args.NewPosition);
            _viewDirty.Add((newMap.MapId, newMap.Position));
        }
    }

    private void OnViewRelevantGone(Entity<ViewRelevantComponent> ent, ref ComponentShutdown args)
    {
        // The transform may already be gone at shutdown; positionless removals are caught
        // by the per-tick deleted-entity filter instead.
        if (!TerminatingOrDeleted(ent.Owner) || !TryComp(ent.Owner, out TransformComponent? xform))
            return;
        var map = transform.GetMapCoordinates(ent.Owner, xform);
        _viewDirty.Add((map.MapId, map.Position));
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

        var tickers = EntityQueryEnumerator<ViewTickerComponent>();
        while (tickers.MoveNext(out var uid, out var comp))
        {
            // Raise every second, but only recalculate when the ticker moved or the
            // cached view went stale — line of sight raycasts are expensive.
            var position = transform.GetMapCoordinates(uid);
            var effectiveRange = Math.Max(comp.range, comp.auraRange);
            var stale = timing.CurTime - comp.lastTickTime >= StaleAfter;
            if (!stale && _viewDirty.Count > 0)
            {
                // A relevant entity moved or vanished inside (or into) this ticker's range.
                var origin = comp.lastTickPosition ?? position;
                var rangeSquared = effectiveRange * effectiveRange;
                foreach (var (mapId, point) in _viewDirty)
                {
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
        _viewDirty.Clear();
    }
}
