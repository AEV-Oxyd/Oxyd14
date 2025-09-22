
/// </summary>
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Content.Shared.Damage;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
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
    [Dependency] protected readonly INetManager _netmanager = default!;
    [Dependency] protected readonly DamageableSystem _damage = default!;
    //[Dependency] private readonly EntityManager _entityManager = default!;
    public List<Entity<OxydProjectileComponent>> Projectiles = new List<Entity<OxydProjectileComponent>>();
    public List<Entity<OxydProjectileComponent>> FireNextTick =  new List<Entity<OxydProjectileComponent>>();
    private float tickDelay = 0;

    public override void Initialize()
    {
        base.Initialize();
        tickDelay = 1000.0f/(float)_gameTiming.TickRate;
        SubscribeLocalEvent<OxydProjectileComponent, StartCollideEvent>(onCollide);
    }

    public virtual bool shouldTriggerCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        if (!TryComp<FixturesComponent>(obj, out var fixtures))
            return false;
        if (args.OurFixture != fixtures.Fixtures.Values.First())
            return false;
        return false;
    }


    public virtual void afterBulletCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        QueueDel(obj);
    }

    public void onCollide(Entity<OxydProjectileComponent> obj, ref StartCollideEvent args)
    {
        if(!shouldTriggerCollide(obj, ref args))
            return;
        if (TryComp<OxydProjectileApplyDamageComponent>(obj, out var damage))
            _damage.TryChangeDamage(args.OtherEntity, damage.DamageSpecifier, false, true);
        afterBulletCollide(obj, ref args);


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
