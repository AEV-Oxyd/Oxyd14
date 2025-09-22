using Content.Server.Effects;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;


namespace Content.Server._Crescent.HullrotGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly ColorFlashEffectSystem  _flashEffectSystem = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void afterBulletCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        _flashEffectSystem.RaiseEffect(Color.Red, new List<EntityUid>(){args.OtherEntity}, Filter.Pvs(args.OtherEntity));
        base.afterBulletCollide(obj, ref args);
    }

    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            Log.Debug($"Speed is {projectile.Comp.initialMovement}");
            _transform.SetMapCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physicsSystem.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, true);
            _physicsSystem.SetLinearDamping(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), 0, true);
            _physicsSystem.SetSleepingAllowed(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), false, true);
            _physicsSystem.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement, true);
        }
    }
}
