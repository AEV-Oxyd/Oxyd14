using System.Numerics;
using Content.Client.Effects;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared._Oxyd.Predictors;
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
    [ViewVariables]
    public Vector2? truePos = Vector2.Zero;

    [ViewVariables]
    public EntityUid lastParent = EntityUid.Invalid;

    [ViewVariables]
    public Vector2 lastWorld = Vector2.Zero;

    [ViewVariables]
    public TimeSpan lastProcessed = TimeSpan.Zero;
}

[RegisterComponent]
public sealed partial class ForcePredictionComponent : Component
{

}


/// <summary>
/// This handles...
/// </summary>
public sealed partial  class FixClientsidePhysicsSystem : VirtualController
{
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  TransformSystem _transform = default!;
    [Dependency] private  Robust.Client.Physics.PhysicsSystem _physics = default!;
    [Dependency] private  BasicPhysicsPredictorSystem _predictor = default!;

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
        ent.Comp.lastParent = Transform(ent).ParentUid;
        ent.Comp.lastWorld = _transform.GetWorldPosition(ent);
        ent.Comp.lastProcessed = _timing.CurTime;
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
        var qery = AllEntityQuery<FixClientsidePhysicsComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            var t = Transform(uid);
            if (t.ParentUid == EntityUid.Invalid)
                continue;
            if (comp.lastParent != t.ParentUid)
            {
                _transform.SetWorldPosition((uid, t), comp.lastWorld);
            }
            else if(comp.truePos is not null)
                _transform.SetLocalPositionNoLerp(uid, comp.truePos.Value);
            // parent changes must be handled here on the first tick
            // couldnt figure out another way SPCR 2026
            if (_timing.IsFirstTimePredicted)
            {
                comp.lastParent = t.ParentUid;
                comp.lastWorld = _transform.GetWorldPosition(uid);
            }

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
            var t = Transform(uid);
            comp.truePos = t.NextPosition;
            comp.lastWorld = _transform.GetWorldPosition(t);
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
    // if client skips tick catch up!!!!!!! SPCR 2026
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var qery = EntityManager.AllEntityQueryEnumerator<FixClientsidePhysicsComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime - comp.lastProcessed >= _timing.TickPeriod * 2)
            {
                var tickc = (int)Math.Floor((_timing.CurTime - comp.lastProcessed) / _timing.TickPeriod) - 1;
                _transform.SetWorldPosition(uid, _predictor.PredictWorldPosition(uid, (uint)tickc));
            }
            comp.lastProcessed = _timing.CurTime;
        }

    }
}
