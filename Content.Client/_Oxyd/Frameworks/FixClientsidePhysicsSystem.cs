using System.Numerics;
using Content.Client.Effects;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Friction;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.Framework;



[RegisterComponent]
public sealed partial class FixClientsidePhysicsComponent : Component
{
    public Vector2 truePos = Vector2.Zero;
}

[RegisterComponent]
public sealed partial class ForcePredictionComponent : Component
{

}


/// <summary>
/// This handles...
/// </summary>
public sealed class FixClientsidePhysicsSystem : VirtualController
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly Robust.Client.Physics.PhysicsSystem _physics = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    public override void Initialize()
    {
        base.Initialize();
        var beforeAr = new[] { typeof(Robust.Client.Physics.PhysicsSystem), typeof(TileFrictionController) };
        SubscribeLocalEvent<FixClientsidePhysicsComponent, UpdateIsPredictedEvent>(OnUpdatePred, beforeAr);
        SubscribeLocalEvent<FixClientsidePhysicsComponent, ComponentStartup>(onPhysStart, beforeAr);
        SubscribeLocalEvent<ForcePredictionComponent, UpdateIsPredictedEvent>(OnUpdatePred,beforeAr);
        SubscribeLocalEvent<ForcePredictionComponent, ComponentStartup>(OnStart, beforeAr);

        UpdatesBefore.Add(typeof(TransformSystem));
        UpdatesBefore.Add(typeof(Robust.Client.Physics.PhysicsSystem));
    }

    public void onPhysStart(Entity<FixClientsidePhysicsComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.truePos = Transform(ent).LocalPosition;
    }

    public void OnStart(Entity<ForcePredictionComponent> ent, ref ComponentStartup args)
    {
        _physics.UpdateIsPredicted(ent);
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        //if (_config.GetCVar(CVars.NetTickrate) == _config.GetCVar(CVars.TargetMinimumTickrate))
        //    return;

        base.UpdateBeforeSolve(prediction, frameTime);
        var qery = EntityManager.AllEntityQueryEnumerator<FixClientsidePhysicsComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            var transf = Transform(uid);
            _transform.SetLocalPositionNoLerp(uid, comp.truePos);
            transf.PredictedLerp = false;
        }
    }

    public override void UpdateAfterSolve(bool prediction, float frameTime)
    {
        base.UpdateAfterSolve(prediction, frameTime);
        if (!_timing.IsFirstTimePredicted)
            return;
        var qery = EntityManager.AllEntityQueryEnumerator<FixClientsidePhysicsComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            var thing = Transform(uid).NextPosition;
            if(thing is not null)
                comp.truePos = thing.Value;
        }

    }

    public void OnUpdatePred(EntityUid ent, IComponent comp, ref UpdateIsPredictedEvent ev)
    {
        ev.IsPredicted = true;
    }

    public void startForcedPrediction(EntityUid entity)
    {
        _physics.SetSleepingAllowed(entity,Comp<PhysicsComponent>(entity), false, false);
        _physics.UpdateIsPredicted(entity);
        EnsureComp<FixClientsidePhysicsComponent>(entity);
    }
}
