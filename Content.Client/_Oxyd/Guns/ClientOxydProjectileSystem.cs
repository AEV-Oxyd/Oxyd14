using Content.Client.Projectiles;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Shared.Physics.Components;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly AnimationPlayerSystem _player = default!;

    public Dictionary<int, Entity<OxydProjectileComponent>> predictedProjectiles = new Dictionary<int, Entity<OxydProjectileComponent>>();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydProjectileComponent, UpdateIsPredictedEvent>(OnUpdatePred);
    }

    public void OnUpdatePred(Entity<OxydProjectileComponent> ent, ref UpdateIsPredictedEvent ev)
    {
        ev.IsPredicted = true;
        ev.BlockPrediction = false;
    }
    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            _transform.SetCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physics.UpdateIsPredicted(projectile.Owner);
            _physics.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, false);
            _physics.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement);
        }
    }
}
