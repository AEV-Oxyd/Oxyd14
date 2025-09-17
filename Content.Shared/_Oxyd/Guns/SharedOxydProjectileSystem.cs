
/// </summary>
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public class OxydProjectileFiredEvent : EntityEventArgs
{
    public NetCoordinates shootingPosition;
    public NetCoordinates targetPosition;
    public NetEntity weapon;
    public NetEntity shooter;
    public int projectileKey;
}


/// <summary>
/// This handles...
/// </summary>
public abstract class SharedOxydProjectileSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming _gameTiming = default!;
    [Dependency] protected readonly SharedTransformSystem _transform = default!;
    [Dependency] protected readonly SharedOxydGunSystem _Guns = default!;
    [Dependency] protected readonly SharedPhysicsSystem _physics = default!;
    //[Dependency] private readonly EntityManager _entityManager = default!;
    public List<Entity<OxydProjectileComponent>> Projectiles = new List<Entity<OxydProjectileComponent>>();
    public List<Entity<OxydProjectileComponent>> FireNextTick =  new List<Entity<OxydProjectileComponent>>();
    private float tickDelay = 0;

    public override void Initialize()
    {
        base.Initialize();
        tickDelay = 1000.0f/(float)_gameTiming.TickRate;
    }

    public void queueProjectile(Entity<OxydProjectileComponent> projectile)
    {
        FireNextTick.Add(projectile);
        projectileQueued(projectile);
    }
    public abstract void projectileQueued(Entity<OxydProjectileComponent> projectile);
    public abstract void processProjectiles(float deltaTime);
    public override void Update(float  deltaTime)
    {
        processProjectiles(deltaTime);
        FireNextTick.Clear();
    }
}
