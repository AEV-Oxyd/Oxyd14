

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Formats.Tar;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Shared._Oxyd.Framework;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.EntityList;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Random.Helpers;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;


public record struct OxydFireDataWrap(GunFiremodePrototype firemode,Entity<OxydGunComponent> gun, EntityUid? shooter);
[Prototype("oxydGunConfig")]
public class OxydGunConfig : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField]
    public SpriteSpecifier safetyOn = default!;

    [DataField]
    public SpriteSpecifier safetyOff = default!;
}
/// <summary>
/// This handles...
/// </summary>
public abstract partial class SharedOxydGunSystem : EntitySystem
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
    [Dependency] protected readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] protected readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] protected readonly SharedAudioSystem _audio = default!;

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";

    private const string magazineContainerName = "Oxyd_Magazine";

    private const string configPrototype = "OxydGunConfig";

    protected const string oxydContents = "storagebase";

    // in milisecunde
    private const float maxAcceptableFireGap = 500;

    protected HashSet<OxydFireDataWrap> checkActive = new();

    public ResPath getSafetySprite(bool toggle)
    {
        var prot = _prototypeManager.Index<OxydGunConfig>("gunConfig");
        if (toggle)
            return OxydHelpers.getSpritePath(prot.safetyOn);
        return OxydHelpers.getSpritePath(prot.safetyOff);

    }


    public override void Initialize()
    {
        InitRecoil();
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydGunAmmoMagazineChamberComponent, ComponentInit>(onMagazineChamberInit);
        SubscribeLocalEvent<OxydGunAmmoMagazineChamberComponent, EntInsertedIntoContainerMessage>(OnEntInsertMag);

    }

    public void OnEntInsertMag(Entity<OxydGunAmmoMagazineChamberComponent> ent,
        ref EntInsertedIntoContainerMessage args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        if (!HasComp<OxydMagazineComponent>(args.Entity))
            return;
        var targetIndex = -1;
        foreach (var slot in ent.Comp.magazineSlot)
        {
            if (slot.ContainerSlot is null)
                continue;
            Log.Debug($"Comparing slot container {slot.ContainerSlot.ID} with {args.Container.ID}");
            if (slot.ContainerSlot.ID == args.Container.ID)
                targetIndex =
                    ent.Comp.magazineSlot.FindIndex(itemSlot => itemSlot.ContainerSlot!.ID == slot.ContainerSlot.ID);
            Log.Debug($"Got target {targetIndex}");
        }

        if (targetIndex == -1)
        {
            Log.Error($"Entity {ent} had a mag inserted for a magazine slot without a linked slot!");
            return;
        }

        if (ent.Comp.bulletSlot[targetIndex].HasItem)
            return;
        CycleMag(targetIndex, ent);
    }

    public bool TryDoFiremodeSwitch(Entity<OxydGunComponent> gun, EntityUid initiator)
    {
        if (gun.Comp.selectedFiremodePrototype.Active)
            return false;
        var gcomp = gun.Comp;
        gcomp.selectedFiremodeIndex = (++gcomp.selectedFiremodeIndex) % gcomp.InstanciatedFiremodes.Count;
        Log.Debug($"Switched firemode to {gcomp.selectedFiremodeIndex}");
        return true;
    }

    public bool TryDoSafetySwitch(Entity<OxydGunComponent> gun, EntityUid initiator)
    {
        if (gun.Comp.selectedFiremodePrototype.Active)
            return false;
        gun.Comp.safety = !gun.Comp.safety;
        Log.Debug($"Switched safety to  {gun.Comp.safety}");
        return true;
    }

    public void onMagazineChamberInit(Entity<OxydGunAmmoMagazineChamberComponent> ent, ref ComponentInit args)
    {
        var index = 0;
        while(index < ent.Comp.bulletSlot.Count)
        {
            _itemSlotsSystem.AddItemSlot(ent.Owner, $"{ammoChamberContainerName}{index}", ent.Comp.bulletSlot[index]);
            _itemSlotsSystem.AddItemSlot(ent.Owner, $"{magazineContainerName}{index}", ent.Comp.magazineSlot[index]);
            ent.Comp.nextBullet.Add(EntityUid.Invalid);
            index++;
        }
    }
    public void onChamberInitialized(Entity<OxydGunAmmoChamberComponent> chamber, ref ComponentInit args)
    {
        var index = 0;
        while (index < chamber.Comp.bulletSlot.Count)
        {
            _itemSlotsSystem.AddItemSlot(chamber.Owner, ammoChamberContainerName, chamber.Comp.bulletSlot[index]);
            chamber.Comp.nextBullet.Add(EntityUid.Invalid);
            index++;
        }
    }

    public void CycleMag(int index, Entity<OxydGunAmmoMagazineChamberComponent> a)
    {
        var targetEnt = a.Comp.magazineSlot[index].Item;
        if (targetEnt is null)
            return;
        var magComp = Comp<OxydMagazineComponent>(targetEnt.Value);
        if (magComp.loadedBullets.Count == 0)
            return;
        var cnt = _containerSystem.GetContainer(targetEnt.Value, oxydContents);
        var ent = GetEntity(magComp.loadedBullets.Pop());
        if (ent == EntityUid.Invalid)
        {
            Log.Debug($"Invalid entity popped in cycle mag!");
            return;
        }
        _containerSystem.Remove(ent, cnt, true, true);
        if (!_itemSlotsSystem.TryInsert(a.Owner, a.Comp.bulletSlot[index], ent, null))
        {
            Log.Debug($"Failed to insert {ent} at {_gameTiming.CurTime}");
            magComp.loadedBullets.Push(GetNetEntity(ent));
            _containerSystem.Insert(ent, cnt, null, true);
            return;
        }
        a.Comp.nextBullet[index] = ent;
        Log.Debug($"Inserted! {ent} at {_gameTiming.CurTime}");
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



    public Vector2 GetBulletInitialMovementDirection(Entity<OxydProjectileComponent> projectile, Entity<OxydGunComponent> gun,  MapCoordinates shootingFrom, MapCoordinates targetPos, EntityUid shooter)
    {
        var firemode = gun.Comp.selectedFiremodePrototype;
        var seed = SharedRandomExtensions.HashCodeCombine( new int[]{ GetNetEntity(gun).Id, (int)gun.Comp.timesFired });
        //Log.Error($"Seed is {seed}");
        var rand = new System.Random(seed);
        var ev = new GunGetInaccuracyEvent()
        {
            addedInaccuracy = firemode.addedInaccuracyMaximum,
            baseInaccuracy = firemode.baseInaccuracy,
            simTick = gun.Comp.simulateAsTick
        };
        RaiseLocalEvent(gun.Owner, ev);
        if(shooter != gun.Owner)
            RaiseLocalEvent(shooter, ev);
        Log.Debug($"b: {ev.baseInaccuracy.Degrees}, a: {ev.addedInaccuracy.Degrees}");
        var inaccuracyDebuff = (ev.baseInaccuracy + rand.NextSingle() * ev.addedInaccuracy);
        inaccuracyDebuff *= rand.NextSingle() > 0.5f ? 1 : -1;
        //Log.Debug($"{inaccuracyDebuff.Degrees} shotCount of {gun.Comp.timesFired} at tick {_gameTiming.CurTick} , realTime {_gameTiming.RealTime}");
        return ((targetPos.Position - shootingFrom.Position).Normalized().ToAngle() + inaccuracyDebuff).ToVec();
    }

    public bool getProjectile(EntityUid shooter, Entity<OxydGunComponent> gun, Entity<OxydBulletComponent> bullet,[NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp)
    {
        outputComp = null;
        EntityUid projectile = Spawn(bullet.Comp.projectileEntity.ToString(), MapCoordinates.Nullspace);
        if (!TryComp<OxydProjectileComponent>(projectile, out var projectileComp))
            return false;
        var speedMult = gun.Comp.selectedFiremodePrototype.SpeedMultiplier;
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        projectileComp.initialMovement = new Vector2(bullet.Comp.Speed * speedMult, bullet.Comp.Speed * speedMult);
        outputComp = (projectile, projectileComp);
        return true;
    }

    public bool getProjectileChambered(EntityUid shooter, Entity<OxydGunComponent> gun,[NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp)
    {
        outputComp = null;
        var firemode = gun.Comp.selectedFiremodePrototype;
        if (!firemode.AmmoProviders.getAmmo(gun.Comp.selectedFiremodePrototype.providerId, out var chambered, out var slot))
            return false;
        if (!TryComp<OxydBulletComponent>(chambered, out var bulletComp))
            return false;
        EntityUid projectile = Spawn(bulletComp.projectileEntity.ToString(), MapCoordinates.Nullspace);
        endChambering(gun);
        var projectileComp = EnsureComp<OxydProjectileComponent>(projectile);
        if (TryComp<OxydGunChargeupComponent>(gun, out var chargeComp))
        {
            if (TryComp<OxydProjectileApplyDamageComponent>(projectile, out var damageComp))
            {
                damageComp.DamageSpecifier *= 1 + ((chargeComp.charge+0.001) / chargeComp.maxCharge) * chargeComp.chargeToMultRatio;
            }

        }
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        projectileComp.initialMovement = new Vector2(bulletComp.Speed * firemode.SpeedMultiplier, bulletComp.Speed * firemode.SpeedMultiplier);
        outputComp = (projectile, projectileComp);
        return true;
    }

    public void endChambering(Entity<OxydGunComponent> gun)
    {
        var index = gun.Comp.selectedFiremodePrototype.providerId;
        switch (gun.Comp.selectedFiremodePrototype.AmmoProviders)
        {
            case OxydGunAmmoMagazineChamberComponent a:
            {
                if (!_itemSlotsSystem.TryEject(gun, a.bulletSlot[index], null, out var ejected))
                    break;
                a.nextBullet[index] = EntityUid.Invalid;
                Log.Debug($"Ejected {ejected} on tick {_gameTiming.CurTick} at {_gameTiming.CurTime}");
                CycleMag(index,(gun.Owner, a));


                break;
            }
            case OxydGunAmmoChamberComponent a:
            {
                _itemSlotsSystem.TryEject(gun, a.bulletSlot[index], null, out var ejected);
                a.nextBullet[index] = EntityUid.Invalid;
                break;
            }
            default:
                break;
        }
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
        while (gun.Comp.firingTime >= gunFiremodePrototype.fireDelay)
        {
            if(!getProjectileChambered(shooter, gun, out var projectileNullable))
                return projectiles;
            var shootSound = gunFiremodePrototype.fireSound;
            var shootEv = new GunBeforeFireIndividualProjectileEvent()
            {
                projectile = projectileNullable.Value,
                simTick = gun.Comp.simulateAsTick
            };
            RaiseLocalEvent(gun.Owner, shootEv);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, shootEv);
            gun.Comp.firingTime -= gunFiremodePrototype.fireDelay;
            Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
            projectile.Comp.initialMovement *= gunFiremodePrototype.SpeedMultiplier;
            projectile.Comp.initialMovement *= GetBulletInitialMovementDirection(projectile, gun, shootingFrom, targetPos, shooter);
            projectile.Comp.initialPosition = shootingFrom.Offset(projectile.Comp.initialMovement * sameTickCounter * (float)gunFiremodePrototype.fireDelay.TotalSeconds);
            _transformSystem.SetWorldRotationNoLerp(projectile.Owner, projectile.Comp.initialMovement.ToAngle());
            projectile.Comp.aimedPosition = targetPos;
            projectiles.Add(projectile);
            _projectileSystem.queueProjectile(projectile);
            gun.Comp.timesFired++;
            sameTickCounter++;
            var afterEv = new GunAfterFireIndividualProjectileEvent()
            {
                projectile = projectileNullable.Value,
                simTick = gun.Comp.simulateAsTick
            };
            _audio.PlayEntity(_audio.ResolveSound(shootSound), Filter.PvsExcept(shooter, 2F), gun.Owner, true);
            RaiseLocalEvent(gun.Owner, afterEv);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, afterEv);
        }

        RaiseLocalEvent(gun.Owner, new GunFiredEvent()
        {
            projectiles = projectiles,
            simTick = gun.Comp.simulateAsTick
        });

        // fallback
        if (gun.Comp.timesFired > int.MaxValue - 1000)
        {
            gun.Comp.timesFired = 0;
        }
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
        var radius = fixtHolder.Fixtures.Values.First().Shape.Radius;
        var mapOffset = (targetPos.Position - map.Position).Normalized();
        mapOffset *= radius;
        return map.Offset(mapOffset);
    }

    public bool tryGetProvider(EntityUid from,[NotNullWhen(true)] out OxydGunProvidersComponent? provider)
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
            var newFiremode = _prototypeManager.Index<GunFiremodePrototype>(proto).createCopy();
            if (!_factory.TryGetRegistration(newFiremode.providerComp, out var registration))
            {
                Log.Debug($"Invalid ammoprovider component {newFiremode.providerComp} for firemode prototype {proto}");
                continue;
            }

            if (!EntityManager.TryGetComponent(gun.Owner, registration, out var providerComp))
            {
                Log.Debug($"Gun Prototype {MetaData(gun.Owner).EntityPrototype} did not have the required providerComp {newFiremode.providerComp} forfiremode proto {proto}");
                continue;
            }

            newFiremode.AmmoProviders = (OxydGunProvidersComponent)providerComp;

            gun.Comp.InstanciatedFiremodes.Add(newFiremode);

        }
        gun.Comp.selectedFiremodeIndex = 0;
    }


    public void onEmptyShootAttempt()
    {

    }

    public void onInvalidShootAttempt()
    {

    }

    public void onSafetyShootAttempt()
    {

    }

    public virtual List<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (gun.Comp.safety)
        {
            onSafetyShootAttempt();
            return null;
        }
        if (!gun.Comp.selectedFiremodePrototype.AmmoProviders.getAmmo(gun.Comp.selectedFiremodePrototype.providerId, out var bullet, out var itemSlot))
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

    public void EnsureActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = true;
    }
    public void RemoveActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = false;
    }


    public bool TryExecuteFiremodeCycle(GunFiremodePrototype firemodePrototype, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        if (firemodePrototype.nextFire > _gameTiming.CurTime)
            return false;
        if (firemodePrototype.lastInterpret == _gameTiming.CurTick)
            return false;
        firemodePrototype.Active = true;
        firemodePrototype.lastInterpret = _gameTiming.CurTick;
        while (firemodePrototype.currentStep < firemodePrototype.maxSteps)
        {
            //Log.Debug($"Interpreting step {firemodePrototype.currentStep} of {firemodePrototype.maxSteps} , step is {firemodePrototype.Effects[firemodePrototype.currentStep]} at tick {_gameTiming.CurTick}, time is {_gameTiming.CurTime}");
            if (!InterpretStep(firemodePrototype, firemodePrototype.Effects[firemodePrototype.currentStep], gun, shooter))
            {
                break;
            }
            firemodePrototype.currentStep++;
        }

        if (firemodePrototype.currentStep == firemodePrototype.maxSteps)
        {
            firemodePrototype.currentStep = 0;
            firemodePrototype.Active = false;
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        HandleActiveRecoil();
    }
}
