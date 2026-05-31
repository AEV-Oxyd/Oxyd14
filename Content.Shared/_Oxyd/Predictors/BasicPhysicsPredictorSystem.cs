using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Predictors;

/// <summary>
/// This handles...
/// </summary>
public abstract partial class BasicPhysicsPredictorSystem : EntitySystem
{
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  RayCastSystem _raycast = default!;

    private EntityQuery<PhysicsComponent> phys;
    private EntityQuery<TransformComponent> transf;
    private EntityQuery<FixturesComponent> fixt;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        phys = GetEntityQuery<PhysicsComponent>();
        transf = GetEntityQuery<TransformComponent>();
        fixt = GetEntityQuery<FixturesComponent>();
        SubscribeLocalEvent<UseBasicPredictionComponent, PlayerAttachedEvent>(onAttach);
    }

    public virtual void onAttach(Entity<UseBasicPredictionComponent> ent, ref PlayerAttachedEvent args)
    {
        var ensure = EnsureComp<ApplyVisualOffsetComponent>(ent);
        ensure.localControl = true;
    }
    public Vector2 PredictWorldPosition(EntityUid target, uint ticks)
    {
        if (!phys.TryComp(target, out var physComp))
            return Vector2.Zero;
        if (!transf.TryComp(target, out var transComp))
            return Vector2.Zero;
        if (ticks == 0)
            return _transform.GetWorldPosition(target);
        var futurePos = (float)_timing.TickPeriod.TotalSeconds * ticks * physComp.LinearVelocity;
        if (fixt.TryComp(target, out var fixtComp))
        {
            var physData = fixtComp.Fixtures.Values.First();
            QueryFilter filter = new QueryFilter();
            filter.LayerBits = physData.CollisionLayer;
            filter.MaskBits = physData.CollisionMask;
            filter.IsIgnored = uid => uid == target;
            var results = _raycast.CastShape(_transform.GetMapId(target), physData.Shape, _physics.GetPhysicsTransform(target, transComp), futurePos, filter , RayCastSystem.RayCastClosestCallback);
            if (results.Hit)
            {
                var hit = results.Results.First();
                return hit.Point - futurePos.Normalized() * physData.Shape.Radius;
            }
        }
        return futurePos + _transform.GetWorldPosition(target);
    }

    public Vector2 PredictWorldOffset(EntityUid target, uint ticks)
    {
        return PredictWorldPosition(target, ticks) - _transform.GetWorldPosition(target);
    }
}
