

using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Random.Helpers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.OxydGunSystem;



/// <summary>
/// This handles...
/// </summary>
public abstract class SharedOxydGunSystem : EntitySystem
{

    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] protected readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedOxydProjectileSystem _projectileSystem = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] protected readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydGunAmmoChamberComponent, ComponentInit>(onChamberInitialized);
    }

    public bool InterpretStep(OxydBaseGunFiremode firemode, OxydGunEffect effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        Log.Error($"Unimplemented Gun Effect tried to be interpreted. Effect: {effect} , IsServer {_netManager.IsServer}");
        return false;
    }


    public bool InterpretStep(OxydBaseGunFiremode firemode, GunEffectTryFireGunDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        MapCoordinates gunCoords = _transformSystem.GetMapCoordinates(gun.Owner);
        if (TryFireGunAt(gun,
                gun.Owner,
                gunCoords.Offset(_transformSystem.GetWorldRotation(gun).ToWorldVec()),
                gunCoords) is null)
        {
            firemode.currentStep = 0;
            return false;
        }

        return true;
    }



    public Vector2 GetBulletInitialMovementDirection(Entity<OxydProjectileComponent> projectile, Entity<OxydGunComponent> gun,  MapCoordinates shootingFrom, MapCoordinates targetPos)
    {
        var firemode = gun.Comp.selectedFiremode;
        var seed = SharedRandomExtensions.HashCodeCombine(new() { (int)_gameTiming.CurTick.Value, GetNetEntity(gun).Id, (int)gun.Comp.timesFired });
        var rand = new System.Random(seed);
        var inaccuracyDebuff = (firemode.baseInaccuracy + rand.NextSingle() * firemode.addedInaccuracyMaximum);
        inaccuracyDebuff *= rand.NextSingle() > 0.5f ? 1 : -1;
        Log.Debug($"{inaccuracyDebuff.Degrees}");
        return ((targetPos.Position - shootingFrom.Position).Normalized().ToAngle() + inaccuracyDebuff).ToVec();
    }

    public bool getProjectile(EntityUid shooter, Entity<OxydGunComponent> gun, Entity<OxydBulletComponent> bullet,[NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp)
    {
        outputComp = null;
        EntityUid projectile = Spawn(bullet.Comp.projectileEntity.ToString(), MapCoordinates.Nullspace);
        if (!TryComp<OxydProjectileComponent>(projectile, out var projectileComp))
            return false;
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        projectileComp.initialMovement = new Vector2(bullet.Comp.Speed * gun.Comp.SpeedMultiplier, bullet.Comp.Speed * gun.Comp.SpeedMultiplier);
        outputComp = (projectile, projectileComp);
        return true;
    }

    public bool getProjectileChambered(EntityUid shooter, Entity<OxydGunComponent> gun,[NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp)
    {
        outputComp = null;
        if (!gun.Comp.ammoProvider.getAmmo(out var chambered, out var slot))
            return false;
        if (!TryComp<OxydBulletComponent>(chambered, out var bulletComp))
            return false;
        EntityUid projectile = Spawn(bulletComp.projectileEntity.ToString(), MapCoordinates.Nullspace);
       // _itemSlotsSystem.TryEject(gun, slot, null, out var ejected);
        var projectileComp = EnsureComp<OxydProjectileComponent>(projectile);
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        projectileComp.initialMovement = new Vector2(bulletComp.Speed * gun.Comp.SpeedMultiplier, bulletComp.Speed * gun.Comp.SpeedMultiplier);
        outputComp = (projectile, projectileComp);
        return true;
    }

    public List<Entity<OxydProjectileComponent>> fireGun(EntityUid shooter, Entity<OxydGunComponent> gun, MapCoordinates shootingFrom, MapCoordinates targetPos)
    {
        OxydBaseGunFiremode gunFiremode = gun.Comp.selectedFiremode;
        gunFiremode.nextFire =  _gameTiming.CurTime + gunFiremode.fireDelay;
        gun.Comp.firingTime += gunFiremode.fireDelay;
        if (gunFiremode.fireDelay < _gameTiming.TickPeriod)
        {
            gun.Comp.firingTime += (_gameTiming.TickPeriod - gunFiremode.fireDelay);
        }
        List<Entity<OxydProjectileComponent>> projectiles = new();
        var sameTickCounter = 0;
        while (gun.Comp.firingTime > gunFiremode.fireDelay)
        {
            if(!getProjectileChambered(shooter, gun, out var projectileNullable))
                return projectiles;
            gun.Comp.firingTime -= gunFiremode.fireDelay;
            Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
            projectile.Comp.initialMovement *= gunFiremode.SpeedMultiplier;
            projectile.Comp.initialMovement *= GetBulletInitialMovementDirection(projectile, gun, shootingFrom, targetPos);
            projectile.Comp.initialPosition = shootingFrom.Offset(projectile.Comp.initialMovement * sameTickCounter * (float)gunFiremode.fireDelay.TotalSeconds);
            projectile.Comp.aimedPosition = targetPos;
            projectiles.Add(projectile);
            _projectileSystem.queueProjectile(projectile);
            gun.Comp.timesFired++;
            sameTickCounter++;
        }

        // fallback
        if (gun.Comp.timesFired > int.MaxValue - 1000)
        {
            gun.Comp.timesFired = 0;
        }
        // Due to Timing inconsistencies (because of lag, packet processing, there will be slight differences
        // when firing in big quantities , as such it is not that expensive to keep syncing the counter after every tick
        Dirty(gun);
        return projectiles;
    }

    public MapCoordinates resolveFiringPosition(Entity<OxydHandheldGunComponent> obj, MapCoordinates targetPos, EntityUid shooter)
    {
        if(!TryComp<FixturesComponent>(shooter, out var fixtHolder))
            return MapCoordinates.Nullspace;
        var map = _transformSystem.GetMapCoordinates(shooter);
        map.Offset((targetPos.Position - map.Position).Normalized() * fixtHolder.Fixtures.Values.First().Shape.Radius * 2f) ;
        return map;
    }

    public bool tryGetProvider(EntityUid from,[NotNullWhen(true)] out OxydGunProviderComponent? provider)
    {
        provider = null;
        if (TryComp<OxydGunAmmoChamberComponent>(from, out var chambered))
        {
            provider = chambered;
            return true;
        }
        if (TryComp<OxydGunAmmoMagazineChamberComponent>(from, out var magazine))
        {
            provider = magazine;
            return true;
        }
        return false;
    }

    public void onGunInitialized(Entity<OxydGunComponent> gun, ref ComponentInit args)
    {
        if (!tryGetProvider(gun.Owner, out var provider))
        {
            Log.Error($"Gun prototype {MetaData(gun.Owner).EntityPrototype} does not have any GunAmmo components to fetch ammo from!");
            return;
        }
        gun.Comp.ammoProvider = provider;
    }

    public void onChamberInitialized(Entity<OxydGunAmmoChamberComponent> chamber, ref ComponentInit args)
    {
        _itemSlotsSystem.AddItemSlot(chamber.Owner, ammoChamberContainerName, chamber.Comp.bulletSlot);
    }

    public void onEmptyShootAttempt()
    {

    }

    public void onInvalidShootAttempt()
    {

    }

    public void RegisterFiremodeAsActive(EntityUid gun,  OxydBaseGunFiremode firemode)
    {
        var c = EnsureComp<OxydActiveFiremodeUpdatingComponent>(gun);
        c.firemode =  firemode;
    }
    public virtual List<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!gun.Comp.ammoProvider.getAmmo(out var bullet, out var itemSlot))
        {
            onEmptyShootAttempt();
            return null;
        }

        if (!TryComp<OxydBulletComponent>(bullet, out var chambered))
        {
            onInvalidShootAttempt();
            return null;
        }

        if (gun.Comp.selectedFiremode.nextFire > _gameTiming.CurTime)
            return null;

        return fireGun(shooter, gun, firingCoordinates, targetCoordinates);
    }

    public bool TryExecuteFiremodeCycle(OxydBaseGunFiremode firemode, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (firemode.nextFire > _gameTiming.CurTime)
            return false;
        firemode.Active = true;
        while (firemode.currentStep < firemode.maxSteps)
        {
            if (!InterpretStep(firemode, firemode.Effects[firemode.currentStep], gun, shooter))
            {
                return false;
            }
            firemode.currentStep++;
        }
        firemode.currentStep = 0;
        firemode.Active = false;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            TryExecuteFiremodeCycle(active.firemode, active.gun, active.shooter);
        }
    }
}
