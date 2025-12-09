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

        UpdatesBefore.Add(typeof(TransformSystem));
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);
        var qery = EntityManager.AllEntityQueryEnumerator<FixClientsidePhysicsComponent>();
        var setValue = !_timing.IsFirstTimePredicted;

        while (qery.MoveNext(out var uid, out var comp))
        {
            if (TerminatingOrDeleted(uid) || uid == EntityUid.Invalid)
                continue;
            if (!TryComp<MetaDataComponent>(uid, out var meta))
                continue;
            SetPaused(uid, setValue, meta);
        }
    }


    public void OnUpdatePred(Entity<FixClientsidePhysicsComponent> ent, ref UpdateIsPredictedEvent ev)
    {
        ev.IsPredicted = true;
    }

    public void startForcedPrediction(EntityUid entity)
    {
        _physics.SetSleepingAllowed(entity,Comp<PhysicsComponent>(entity), false, false);
        _physics.UpdateIsPredicted(entity);
        EnsureComp<FixClientsidePhysicsComponent>(entity);
    }

    public override void Update(float deltaTime)
    {
        // horrible...
        var qery = EntityQueryEnumerator<FixClientsidePhysicsComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            var transf = Transform(uid);
            transf.PredictedLerp = false;
            //transf.ActivelyLerping = false;
        }

    }
}
