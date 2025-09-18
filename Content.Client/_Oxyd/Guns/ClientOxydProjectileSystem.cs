using System.Numerics;
using Content.Client.Projectiles;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly AnimationPlayerSystem _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public Dictionary<int, Entity<OxydProjectileComponent>> predictedProjectiles = new Dictionary<int, Entity<OxydProjectileComponent>>();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydProjectileComponent, UpdateIsPredictedEvent>(OnUpdatePred);

        UpdatesBefore.Add(typeof(TransformSystem));
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
            projectile.Comp.client = true;
            Log.Debug($"Speed is {projectile.Comp.initialMovement}");
            _transform.SetCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            //_physics.UpdateIsPredicted(projectile.Owner);
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
