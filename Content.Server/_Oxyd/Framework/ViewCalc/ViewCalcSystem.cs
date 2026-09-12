using System.Linq;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Oxyd.Framework.ViewCalc;

public class ViewTickEvent : EntityEventArgs
{
    public required HashSet<EntityUid> seen;
}

/// <summary>Finds the visible entities for each view ticker once per second.</summary>
public sealed class ViewCalcSystem : EntitySystem
{
    [Dependency] private RayCastSystem raycaster = default!;
    [Dependency] private TransformSystem transform = default!;
    [Dependency] private EntityLookupSystem entlook = default!;

    private float passed;

    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range)
    {
        HashSet<EntityUid> keepers = new();
        if (range <= 0)
            return keepers;

        HashSet<Entity<ViewRelevantComponent>> result = new();
        entlook.GetEntitiesInRange(point, range, result);
        var filter = new QueryFilter
        {
            Flags = QueryFlags.Static,
            LayerBits = (int) CollisionGroup.Opaque,
            MaskBits = (int) CollisionGroup.Opaque,
        };
        foreach (var ent in result)
        {
            if (InLineOfSight(point, ent.Owner, filter))
                keepers.Add(ent);
        }
        return keepers;
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
        passed += frameTime;
        if (passed < 1f)
            return;
        passed = 0;

        var tickers = EntityQueryEnumerator<ViewTickerComponent>();
        while (tickers.MoveNext(out var uid, out var comp))
        {
            comp.lastSeen = GetEntsInView(transform.GetMapCoordinates(uid), comp.range);
            RaiseLocalEvent(uid, new ViewTickEvent { seen = comp.lastSeen });
        }
    }
}
