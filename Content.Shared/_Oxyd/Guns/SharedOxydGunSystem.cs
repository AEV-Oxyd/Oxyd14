

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Physics;

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

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";

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

    public void fireGun(EntityUid shooter, Entity<OxydGunComponent> gun, MapCoordinates shootingFrom, MapCoordinates targetPos)
    {
        if(!getProjectileChambered(shooter, gun, out var projectileNullable))
            return;
        Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
        projectile.Comp.initialPosition = shootingFrom;
        projectile.Comp.initialMovement *= (targetPos.Position - shootingFrom.Position).Normalized();
        projectile.Comp.aimedPosition = targetPos;
        _projectileSystem.queueProjectile(projectile);
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

    public void TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!gun.Comp.ammoProvider.getAmmo(out var bullet, out var itemSlot))
        {
            onEmptyShootAttempt();
            return;
        }

        if (!TryComp<OxydBulletComponent>(bullet, out var chambered))
        {
            onInvalidShootAttempt();
            return;
        }
        fireGun(shooter, gun, firingCoordinates, targetCoordinates);
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydGunAmmoChamberComponent, ComponentInit>(onChamberInitialized);
    }
}
