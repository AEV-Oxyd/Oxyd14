using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Framework.ViewCalc;

public class ViewTickEvent : EntityEventArgs
{
    public required HashSet<EntityUid> seen;
}

/// <summary>
/// Raised on a view ticker every second, whether or not the seen set was recomputed.
/// Tickers that only need a cadence subscribe to this and leave
/// <see cref="ViewTickerComponent.trackSeen"/> false.
/// </summary>
public class ViewCadenceEvent : EntityEventArgs
{
}

/// <summary>
/// This handles...
/// </summary>
public sealed class ViewCalcSystem : EntitySystem
{
    [Dependency] private FixtureSystem fixtures = default!;
    [Dependency] private RayCastSystem raycaster = default!;
    [Dependency] private TransformSystem transform = default!;
    [Dependency] private EntityLookupSystem entlook = default!;
    [Dependency] private SharedBroadphaseSystem broadphase = default!;
    [Dependency] private IGameTiming timing = default!;
    public float passed = 0;
    public EntityQueryEnumerator<ViewTickerComponent> tickerEnum = default!;

    public override void Initialize()
    {
    }
    /// <summary>
    /// TODO IF TOO HARD ON PERFORMANCE
    /// List of every ent in square AABB -> sort by angle(calculated from fixture) -> group by angle -> sort by distance -> raycast in order
    /// Alternatively make a version for grid-based view, optimize heavily based off tiles instead
    /// Couldn't be arsed to optimize this the first time im writing it SPCR 2026
    /// </summary>
    /// <param name="point"></param>
    /// <param name="range"></param>
    /// <returns></returns>
    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range)
    {
        HashSet<Entity<ViewRelevantComponent>> result = new();
        entlook.GetEntitiesInRange<ViewRelevantComponent>(point, range, result, LookupFlags.Approximate);
        HashSet<EntityUid> keepers = new();
        foreach (var ent in result)
        {
            if (InLineOfSight(point, ent.Owner))
                keepers.Add(ent);
        }
        return keepers;
    }

    /// <summary>
    /// True when no opaque wall blocks the ray from <paramref name="origin"/> to
    /// <paramref name="target"/>. Uses the same filter as <see cref="GetEntsInView"/>
    /// so callers with their own broad phase get the same answer.
    /// </summary>
    public bool InLineOfSight(MapCoordinates origin, EntityUid target)
    {
        var filter = new QueryFilter()
        {
            Flags = QueryFlags.Static,
            LayerBits = (int)CollisionGroup.Opaque,
            MaskBits = (int)CollisionGroup.Opaque
        };
        var res = raycaster.CastRayClosest(origin.MapId,
            origin.Position,
            transform.GetWorldPosition(target) - origin.Position,
            filter);
        return !res.Hit || res.Results.First().Entity == target;
    }

    public override void Update(float frameTime)
    {
        passed += frameTime;
        if (passed < 1f)
            return;
        passed = 0;
        tickerEnum = EntityQueryEnumerator<ViewTickerComponent>();
        while (tickerEnum.MoveNext(out var uid, out var comp))
        {
            var coord = transform.GetMapCoordinates(uid);
            var recompute = coord.MapId != comp.lastTickPosition.MapId
                || (coord.Position - comp.lastTickPosition.Position).LengthSquared() > 2f
                || timing.CurTime - comp.lastTickTime > TimeSpan.FromSeconds(5);

            if (recompute)
            {
                comp.lastTickTime = timing.CurTime;
                comp.lastTickPosition = coord;

                if (comp.trackSeen)
                {
                    var ev = new ViewTickEvent() { seen = GetEntsInView(coord, comp.range) };
                    RaiseLocalEvent(uid, ev);
                    comp.lastSeen = ev.seen;
                }
            }

            // The heartbeat is separate from the scan. Tickers that only need a
            // cadence do not pay for the raycasts.
            RaiseLocalEvent(uid, new ViewCadenceEvent());
        }
    }
}
