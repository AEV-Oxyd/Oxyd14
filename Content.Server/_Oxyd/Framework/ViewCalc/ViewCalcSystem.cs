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
        var filter = new QueryFilter()
        {
            Flags = QueryFlags.Static,
            LayerBits = (int)CollisionGroup.Opaque,
            MaskBits = (int)CollisionGroup.Opaque
        };
        HashSet<EntityUid> keepers = new();
        foreach (var ent in result)
        {
            var res = raycaster.CastRayClosest(point.MapId,
                point.Position,
                transform.GetWorldPosition(ent) - point.Position,
                filter);
            if (res.Results.First().Entity == ent.Owner || !res.Hit)
                keepers.Add(ent);
        }
        return keepers;
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
            if(coord.MapId != comp.lastTickPosition.MapId)
                goto runUpdate;
            if((coord.Position - comp.lastTickPosition.Position).LengthSquared()> 2f)
                goto runUpdate;
            if (timing.CurTime - comp.lastTickTime > TimeSpan.FromSeconds(5))
                goto runUpdate;
            continue;
            runUpdate:
            comp.lastTickTime = timing.CurTime;
            comp.lastTickPosition = coord;
            var ev = new ViewTickEvent() { seen = GetEntsInView(coord, comp.range) };
            RaiseLocalEvent(uid, ev);
            comp.lastSeen = ev.seen;
        }
    }
}
