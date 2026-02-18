using System.Linq;
using System.Numerics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Predictors;

/// <summary>
/// This handles...
/// </summary>
public sealed class BasicPhysicsPredictorSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly RayCastSystem _raycast = default!;
    [Dependency] private readonly FixtureSystem _fixtures = default!;

    private EntityQuery<PhysicsComponent> phys;
    private EntityQuery<TransformComponent> transf;
    private EntityQuery<FixturesComponent> fixt;

    /// <inheritdoc/>
    public override void Initialize()
    {
        phys = GetEntityQuery<PhysicsComponent>();
        transf = GetEntityQuery<TransformComponent>();
        fixt = GetEntityQuery<FixturesComponent>();
    }

    public Vector2 PredictWorldPosition(EntityUid target, int ticks)
    {
        if (!phys.TryComp(target, out var physComp))
            return Vector2.Zero;
        if (!transf.TryComp(target, out var transComp))
            return Vector2.Zero;
        var mapped = _transform.GetMap(target);
        if(!mapped.HasValue)
            return Vector2.Zero;
        var futurePos = (float)_timing.TickPeriod.TotalSeconds * ticks * physComp.LinearVelocity;
        if (fixt.TryComp(target, out var fixtComp))
        {
            var physData = fixtComp.Fixtures.Values.First();
            QueryFilter filter = new QueryFilter();
            filter.LayerBits = physData.CollisionLayer;
            filter.MaskBits = physData.CollisionMask;
            var results = new RayResult();
            _raycast.CastShape(mapped.Value,ref results, physData.Shape, _physics.GetPhysicsTransform(target, transComp), futurePos, filter , RayCastSystem.RayCastClosestCallback);
            if (results.Hit)
            {
                var hit = results.Results.First();
                return hit.Point;
            }
        }
        return futurePos;
    }
}
