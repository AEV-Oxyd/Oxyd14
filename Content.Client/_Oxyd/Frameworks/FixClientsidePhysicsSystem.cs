using System.Numerics;
using Content.Client.Effects;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FixClientsidePhysicsComponent, UpdateIsPredictedEvent>(OnUpdatePred);
        SubscribeLocalEvent<ForcePredictionComponent, UpdateIsPredictedEvent>(OnUpdatePred);
        SubscribeLocalEvent<ForcePredictionComponent, ComponentStartup>(OnStart);

        UpdatesBefore.Add(typeof(TransformSystem));
        UpdatesBefore.Add(typeof(Robust.Client.Physics.PhysicsSystem));
    }

    public void OnStart(Entity<ForcePredictionComponent> ent, ref ComponentStartup args)
    {
        _physics.UpdateIsPredicted(ent);
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);
        var qery = EntityManager.AllEntityQueryEnumerator<FixClientsidePhysicsComponent>();
        var setValue = !_timing.IsFirstTimePredicted;
        while (qery.MoveNext(out var uid, out var comp))
        {
            SetPaused(uid, setValue);
            //_physics.SetCanCollide(uid, _timing.IsFirstTimePredicted);
            var transf = Transform(uid);
            transf.PredictedLerp = false;
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
