using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Oxyd.Framework.ViewCalc;

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
    /// <summary>
    /// TODO IF TOO HARD ON PERFORMANCE
    /// List of every ent in square AABB -> sort by angle(calculated from fixture) -> group by angle -> sort by distance -> raycast in order
    /// Couldn't be arsed to optimize this the first time im writing it SPCR 2026
    /// </summary>
    /// <param name="point"></param>
    /// <param name="range"></param>
    /// <returns></returns>
    public HashSet<EntityUid> GetEntsInView(MapCoordinates point, float range)
    {
        HashSet<EntityUid> result = new();
        entlook.GetEntitiesInRange<ViewRelevantComponent>(point, range, result);
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
            if (res.Results.First().Entity == ent)
                keepers.Add(ent);
        }
        return keepers;
    }
}
