using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Physics.Components;


namespace Content.Server._Crescent.HullrotGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ServerOxydProjectileSystem : SharedOxydProjectileSystem
{
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
            _physics.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, true);
            _transform.SetCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physics.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement);
        }
    }
}
