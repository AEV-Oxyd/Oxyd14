using System.Numerics;
using Content.Client.Effects;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly AnimationPlayerSystem _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ColorFlashEffectSystem _colorFlashEffect = default!;

    public Dictionary<int, Entity<OxydProjectileComponent>> predictedProjectiles = new Dictionary<int, Entity<OxydProjectileComponent>>();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydProjectileComponent, UpdateIsPredictedEvent>(OnUpdatePred);

        UpdatesBefore.Add(typeof(TransformSystem));
    }

    public override bool shouldTriggerCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        if (!base.shouldTriggerCollide(obj, ref args))
            return false;
        if (TryComp<ClientsidePleaseIgnoreComponent>(obj, out var ignore) && _playerManager.LocalSession is not null &&
            ignore.forSessions.Contains(_playerManager.LocalSession.Name))
            return false;
        return true;

    }

    public override void afterBulletCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        _colorFlashEffect.RaiseEffect(Color.Red, new List<EntityUid>(){args.OtherEntity}, Filter.Local());
        base.afterBulletCollide(obj, ref args);
    }

    public void OnUpdatePred(Entity<OxydProjectileComponent> ent, ref UpdateIsPredictedEvent ev)
    {
        ev.IsPredicted = true;
    }
    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physics.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, false);
            _physics.SetLinearDamping(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), 0f, false);
            _physics.SetSleepingAllowed(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), false, false);
            _physics.SetAngularDamping(projectile.Owner, Comp<PhysicsComponent>(projectile.Owner), 0f, false);
            _physics.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement);
        }
    }

    public override void FrameUpdate(float deltaTime)
    {
    }
}
