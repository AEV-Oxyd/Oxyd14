

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Formats.Tar;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Content.Shared._Oxyd.Framework;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.EntityList;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Random.Helpers;
using Robust.Shared.Audio;
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
public sealed partial class OxydGunConfig : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField]
    public SpriteSpecifier safetyOn = default!;

    [DataField]
    public SpriteSpecifier safetyOff = default!;

    [DataField]
    public SoundSpecifier jammedBallistic = default!;

    [DataField]
    public SoundSpecifier jammedLaser = default!;
}
/// <summary>
/// This handles...
/// </summary>
public abstract partial class SharedOxydGunSystem : EntitySystem
{
    [Dependency] protected readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedOxydProjectileSystem _projectileSystem = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] protected readonly IGameTiming _gameTiming = default!;
    [Dependency] protected readonly INetManager _netManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] protected readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] protected readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] protected readonly IComponentFactory _factory = default!;
    [Dependency] protected readonly SharedAudioSystem _audio = default!;
    [Dependency] protected readonly GunChargeDecaySystem _charge = default!;
    [Dependency] protected readonly SharedBatterySystem _battery = default!;
    [Dependency] protected readonly SharedOxydHelpers _help = default!;
    [Dependency] protected readonly ISharedPlayerManager _players = default!;

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";

    private const string magazineContainerName = "Oxyd_Magazine";

    protected const string oxydContents = "storagebase";

    protected const string configProto = "gunConfig";

    // in milisecunde
    private const float maxAcceptableFireGap = 500;

    protected HashSet<OxydFireDataWrap> checkActive = new();

    public SpriteSpecifier getSafetySprite(bool toggle)
    {
        var prot = _prototypeManager.Index<OxydGunConfig>(configProto);
        if (toggle)
            return prot.safetyOn;
        return prot.safetyOff;
    }

    public SoundSpecifier getJammedSound(bool laser)
    {
        var prot = _prototypeManager.Index<OxydGunConfig>(configProto);
        return laser ? prot.jammedLaser : prot.jammedBallistic;
    }


    public override void Initialize()
    {
        InitRecoil();
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydGunAmmoMagazineChamberComponent, ComponentInit>(onMagazineChamberInit);
        SubscribeLocalEvent<OxydGunAmmoChamberComponent, ComponentInit>(onChamberInitialized);
        SubscribeLocalEvent<OxydGunAmmoMagazineChamberComponent, EntInsertedIntoContainerMessage>(OnEntInsertMag);
        SubscribeLocalEvent<OxydChargeComponent, ComponentInit>(onChargeInit);
        SubscribeLocalEvent<OxydChargeComponent, ChargeChangedEvent>(onBatteryCharge);

    }

    public void onChargeInit(Entity<OxydChargeComponent> ent, ref ComponentInit args)
    {
        if (!TryComp<BatteryComponent>(ent, out var bat))
        {
            Log.Error($"OxydChargeComponent on {ent} has no BatteryComponent!");
            return;
        }
        ent.Comp.charge = bat.StartingCharge;
    }

    public void onBatteryCharge(Entity<OxydChargeComponent> ent, ref ChargeChangedEvent args)
    {
        // cancel update to battery if client-side and active
        if (_netManager.IsClient && _players.LocalEntity is not null)
        {
            if (_help.GetParentWithComp<OxydGunComponent>(ent.Owner, out var target))
            {
                if (_handsSystem.EnumerateHeld((_players.LocalEntity.Value, null))
                    .ToList()
                    .Contains(target.Value.Owner) && target.Value.Comp.selectedFiremodePrototype.Active)
                    return;
            }
        }

        ent.Comp.charge = args.CurrentCharge;
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
            addedInaccuracy = firemode.addedInaccuracyMaximum/2,
            baseInaccuracy = firemode.baseInaccuracy/2,
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

    public bool tryDischargeAmount(EntityUid gun, float amount,[NotNullWhen(true)] out EntityUid? used)
    {
        foreach (var container in _containerSystem.GetAllContainers(gun))
        {
            if (container is not ContainerSlot slot)
                continue;
            if (slot.ContainedEntity is null)
                continue;
            if (!TryComp<BatteryComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (_battery.TryUseCharge((slot.ContainedEntity.Value, batt), amount))
            {
                used = slot.ContainedEntity;
                return true;
            }
        }

        used = null;
        return false;
    }

    public bool hasDischargeAmount(EntityUid gun, float amount)
    {
        foreach (var container in _containerSystem.GetAllContainers(gun))
        {
            if (container is not ContainerSlot slot)
                continue;
            if (slot.ContainedEntity is null)
                continue;
            if (!TryComp<BatteryComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (_battery.GetCharge((slot.ContainedEntity.Value, batt)) >= amount)
                return true;
        }
        return false;
    }

    public bool tryGetProviderAmmo(Entity<OxydGunComponent> gun,
        [NotNullWhen(true)] out EntProtoId? projectile,
        [NotNullWhen(true)] out EntityUid? ammo)
    {
        ammo  = null;
        projectile = null;
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            // magazine uses the same handling
            case OxydGunAmmoChamberComponent provider:
                ammo = provider.nextBullet[frd.providerId];
                if (TerminatingOrDeleted(ammo))
                    return false;
                projectile = Comp<OxydBulletComponent>(ammo.Value).projectileEntity;
                return ammo.Value != EntityUid.Invalid;
            case OxydGunLaserProviderComponent provider:
                projectile = provider.laserProto[frd.providerId].laser;
                if (tryDischargeAmount(gun.Owner, provider.laserProto[frd.providerId].cost, out var used))
                {
                    ammo = used;
                }

                if (ammo is null)
                    return false;
                return ammo.Value != EntityUid.Invalid;
            default:
                Log.Error($"Unimplemented ammoProvider in getProviderAmmo,  type {frd.AmmoProviders}");
                projectile = null;
                return false;
        }
    }

    public bool hasProviderAmmo(Entity<OxydGunComponent> gun, int index)
    {
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            // magazine also fits here
            case OxydGunAmmoChamberComponent provider:
                return provider.nextBullet[index] != EntityUid.Invalid;
            case OxydGunLaserProviderComponent provider:
                return hasDischargeAmount(gun.Owner, provider.laserProto[index].cost);
            default:
                Log.Error($"Unimplemented hasProviderAmmo case ,  type {frd.AmmoProviders}");
                return false;
        }
    }

    public void afterProviderAmmo(Entity<OxydGunComponent> gun, EntityUid bullet)
    {
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            case OxydGunAmmoChamberComponent provider:
                var slot = provider.bulletSlot[frd.providerId];
                if (_itemSlotsSystem.TryEject(gun, slot, null, out _))
                {
                    _help.QueueDel(bullet);
                    provider.nextBullet[frd.providerId] = EntityUid.Invalid;
                    if(provider is OxydGunAmmoMagazineChamberComponent mag)
                        CycleMag(frd.providerId, (gun.Owner, mag));
                }
                break;
            case OxydGunLaserProviderComponent provider:
                break;
            default:
                Log.Error($"Unimplemented afterProviderAmmo case ,  type {frd.AmmoProviders}");
                return;
        }
    }


    public bool getProjectileLoaded(EntityUid shooter, Entity<OxydGunComponent> gun,
        [NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp,
        [NotNullWhen(true)] out EntityUid? used)
    {
        outputComp = null;
        used = null;
        var firemode = gun.Comp.selectedFiremodePrototype;
        if (!tryGetProviderAmmo(gun, out var proj, out var ammoEnt))
            return false;
        EntityUid projectile = Spawn(proj.ToString(), MapCoordinates.Nullspace);
        var projectileComp = EnsureComp<OxydProjectileComponent>(projectile);
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        if (TryComp<OxydBulletComponent>(ammoEnt, out var ammoBullet))
        {
            projectileComp.initialMovement = new Vector2(ammoBullet.Speed, ammoBullet.Speed);
        }
        outputComp = (projectile, projectileComp);
        used = ammoEnt;
        return true;
    }


    public static TimeSpan getTotalWait(GunFiremodePrototype target)
    {
        TimeSpan totalWait = TimeSpan.Zero;
        foreach (var effect in target.Effects)
        {
            switch (effect)
            {
                case GunEffectWait wait:
                    totalWait += wait.waitPeriod;
                    break;
            }
        }
        return totalWait;
    }

    public HashSet<Entity<OxydProjectileComponent>> fireGun(EntityUid shooter,
        Entity<OxydGunComponent> gun,
        MapCoordinates shootingFrom,
        MapCoordinates targetPos)
    {
        GunFiremodePrototype gunFiremodePrototype = gun.Comp.selectedFiremodePrototype;
        var lastFireDelta = _gameTiming.CurTime - gunFiremodePrototype.nextFire - gunFiremodePrototype.totalWait;
        Log.Debug($"Last fire delta is {lastFireDelta}, totalWait {gunFiremodePrototype.totalWait}, gap {gunFiremodePrototype.firingGaps}");
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
        HashSet<Entity<OxydProjectileComponent>> projectiles = new();
        var sameTickCounter = 0;
        if (gunFiremodePrototype.SingleShot && gun.Comp.firingTime >= gunFiremodePrototype.fireDelay * 2)
            gun.Comp.firingTime = gunFiremodePrototype.fireDelay;
        while (gun.Comp.firingTime >= gunFiremodePrototype.fireDelay)
        {
            if(!getProjectileLoaded(shooter, gun, out var projectileNullable, out var used))
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
            afterProviderAmmo(gun, used.Value);
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

    public bool preFireChecks(Entity<OxydGunComponent> gun)
    {
        if (gun.Comp.safety)
        {
            onSafetyShootAttempt();
            return false;
        }
        if (!hasProviderAmmo(gun, gun.Comp.selectedFiremodePrototype.providerId))
        {
            onEmptyShootAttempt();
            return false;
        }

        return true;
    }

    public virtual HashSet<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        var gfp = gun.Comp.selectedFiremodePrototype;
        if (gfp.nextFire > _gameTiming.CurTime)
        {
            Log.Debug("Firemode not ready");
            return null;
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
        Log.Debug($"Queued for active removal {gun.Owner}");
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = false;
    }


    public bool TryExecuteFiremodeCycle(GunFiremodePrototype firemodePrototype, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        Log.Debug($"Executing firecycle at {_gameTiming.CurTick}");
        if (gun.Comp.jammed)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            Log.Debug($"Interpret failed: jam");
            return false;
        }

        if (firemodePrototype.nextFire > _gameTiming.CurTime && _netManager.IsClient)
        {
            Log.Debug($"Interpret failed: nextFire");
            return false;
        }

        if (firemodePrototype.lastInterpreted == _gameTiming.CurTick)
        {
            Log.Debug($"Interpret failed: sameTick");
            return false;
        }

        firemodePrototype.Active = true;
        firemodePrototype.lastInterpreted = _gameTiming.CurTick;
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
