

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Shared._Oxyd.Framework;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Random.Helpers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
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
    [Dependency] protected readonly INetManager _netManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";
    private const float maxAcceptableFireGap = 500;


    public override void Initialize()
    {
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydGunAmmoChamberComponent, ComponentInit>(onChamberInitialized);

    }


    public abstract bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter);


    public bool InterpretStepWithPosition(GunFiremodePrototype firemodePrototype, OxydGunEffect effect, Entity<OxydGunComponent> gun, MapCoordinates firingFrom,
            MapCoordinates towards, EntityUid? shooter)
    {
        Log.Error($"Unimplemented Gun Effect  WITH POSITION tried to be interpreted. Effect: {effect} , IsServer {_netManager.IsServer}");
        return false;
    }


    public bool InterpretStep(GunFiremodePrototype firemodePrototype, GunEffectTryFireGunDirection effect, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        MapCoordinates gunCoords = _transformSystem.GetMapCoordinates(gun.Owner);
        if (TryFireGunAt(gun,
                gun.Owner,
                gunCoords.Offset(_transformSystem.GetWorldRotation(gun).ToWorldVec()),
                gunCoords) is null)
        {
            firemodePrototype.currentStep = 0;
            return false;
        }

        return true;
    }



    public Vector2 GetBulletInitialMovementDirection(Entity<OxydProjectileComponent> projectile, Entity<OxydGunComponent> gun,  MapCoordinates shootingFrom, MapCoordinates targetPos)
    {
        var firemode = gun.Comp.selectedFiremodePrototype;
        var seed = SharedRandomExtensions.HashCodeCombine(new() { GetNetEntity(gun).Id, (int)gun.Comp.timesFired });
        Log.Error($"Seed is {seed}");
        var rand = new System.Random(seed);
        var inaccuracyDebuff = (firemode.baseInaccuracy + rand.NextSingle() * firemode.addedInaccuracyMaximum);
        inaccuracyDebuff *= rand.NextSingle() > 0.5f ? 1 : -1;
        Log.Debug($"{inaccuracyDebuff.Degrees} shotCount of {gun.Comp.timesFired} at tick {_gameTiming.CurTick} , realTime {_gameTiming.RealTime}");
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

    public List<Entity<OxydProjectileComponent>> fireGun(EntityUid shooter,
        Entity<OxydGunComponent> gun,
        MapCoordinates shootingFrom,
        MapCoordinates targetPos)
    {
        GunFiremodePrototype gunFiremodePrototype = gun.Comp.selectedFiremodePrototype;
        var lastFireDelta = _gameTiming.CurTime - gunFiremodePrototype.nextFire;
        gunFiremodePrototype.nextFire =  _gameTiming.CurTime + gunFiremodePrototype.fireDelay;
        gun.Comp.firingTime += gunFiremodePrototype.fireDelay;
        //Log.Debug($"Fire Delta is {lastFireDelta}");
        if (lastFireDelta > gunFiremodePrototype.fireDelay && lastFireDelta < TimeSpan.FromMilliseconds(maxAcceptableFireGap) && gunFiremodePrototype.firingGaps < TimeSpan.FromMilliseconds(maxAcceptableFireGap))
        {
            gunFiremodePrototype.firingGaps += lastFireDelta - gunFiremodePrototype.fireDelay;
            Log.Debug($"Accumulating firegap of {gunFiremodePrototype.firingGaps}");
        }
        gunFiremodePrototype.lastFiredTick = _gameTiming.CurTick;
        if (gunFiremodePrototype.fireDelay < _gameTiming.TickPeriod)
        {
            gun.Comp.firingTime += (_gameTiming.TickPeriod - gunFiremodePrototype.fireDelay);
        }
        List<Entity<OxydProjectileComponent>> projectiles = new();
        var sameTickCounter = 0;
        while (gun.Comp.firingTime > gunFiremodePrototype.fireDelay)
        {
            if(!getProjectileChambered(shooter, gun, out var projectileNullable))
                return projectiles;
            gun.Comp.firingTime -= gunFiremodePrototype.fireDelay;
            Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
            projectile.Comp.initialMovement *= gunFiremodePrototype.SpeedMultiplier;
            projectile.Comp.initialMovement *= GetBulletInitialMovementDirection(projectile, gun, shootingFrom, targetPos);
            projectile.Comp.initialPosition = shootingFrom.Offset(projectile.Comp.initialMovement * sameTickCounter * (float)gunFiremodePrototype.fireDelay.TotalSeconds);
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
        RaiseLocalEvent(new FiremodeProjectilesFiredEvent()
        {
            gun = gun,
            projectiles = projectiles,
            shooter = shooter
        });
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

        foreach (var proto in gun.Comp.firemodes)
        {
            gun.Comp.InstanciatedFiremodes.Add(_prototypeManager.Index<GunFiremodePrototype>(proto).createCopy());
        }
        gun.Comp.ammoProvider = provider;
        gun.Comp.selectedFiremodePrototype = gun.Comp.InstanciatedFiremodes.First();
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
        var gunFiremodePrototype = gun.Comp.selectedFiremodePrototype;
        if (gunFiremodePrototype.nextFire > _gameTiming.CurTime)
        {
            Log.Debug("Firemode not ready");
            return null;
        }
        // compensare lag
        if (gunFiremodePrototype.lastFiredTick == _gameTiming.CurTick)
        {
            Log.Debug("Same tick fire");
            if (gunFiremodePrototype.firingGaps < gunFiremodePrototype.fireDelay)
                return null;
            gunFiremodePrototype.firingGaps -= gunFiremodePrototype.fireDelay;
        }

        return fireGun(shooter, gun, firingCoordinates, targetCoordinates);
    }

    public bool TryExecuteFiremodeCycle(GunFiremodePrototype firemodePrototype, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (firemodePrototype.nextFire > _gameTiming.CurTime)
            return false;
        firemodePrototype.Active = true;
        while (firemodePrototype.currentStep < firemodePrototype.maxSteps)
        {
            //Log.Error($"Interpreting step {firemodePrototype.currentStep} of {firemodePrototype.maxSteps}");
            if (!InterpretStep(firemodePrototype, firemodePrototype.Effects[firemodePrototype.currentStep], gun, shooter))
            {
                return false;
            }
            firemodePrototype.currentStep++;
        }
        firemodePrototype.currentStep = 0;
        firemodePrototype.Active = false;
        return true;
    }
}
