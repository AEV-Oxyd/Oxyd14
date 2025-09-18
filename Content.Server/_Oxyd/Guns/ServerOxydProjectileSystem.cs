using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;


namespace Content.Server._Crescent.HullrotGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {

    }

    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            Log.Debug($"Speed is {projectile.Comp.initialMovement}");
            _transform.SetCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physicsSystem.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, true);
            _physicsSystem.SetLinearDamping(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), 0, true);
            _physicsSystem.SetSleepingAllowed(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), false, true);
            _physicsSystem.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement, true);
        }
    }
}
