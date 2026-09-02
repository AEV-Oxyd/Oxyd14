using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Content.Shared.ActionBlocker;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Random.Helpers;
using Content.Shared.Stacks;
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
    [Dependency] protected  SharedTransformSystem _transformSystem = default!;
    [Dependency] private  SharedOxydProjectileSystem _projectileSystem = default!;
    [Dependency] private  ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] protected  IGameTiming _gameTiming = default!;
    [Dependency] protected  INetManager _netManager = default!;
    [Dependency] private  IPrototypeManager _prototypeManager = default!;
    [Dependency] protected  SharedContainerSystem _containerSystem = default!;
    [Dependency] protected  IComponentFactory _factory = default!;
    [Dependency] protected  SharedAudioSystem _audio = default!;
    [Dependency] protected  GunChargeDecaySystem _charge = default!;
    [Dependency] protected  SharedBatterySystem _battery = default!;
    [Dependency] protected  SharedOxydHelpers _help = default!;
    [Dependency] protected  ISharedPlayerManager _players = default!;
    [Dependency] protected  OxydModifiersSystem _mods = default!;
    [Dependency] protected  SharedHandsSystem _hands = default!;
    [Dependency] protected OxydPredContainerSystem conts = default!;

    private const string chamberStoreKey = "Chamber";
    private const string magazineStoreKey = "Magazine";
    private const string revolverStoreKey = "Revolver";
    public static readonly List<string> AllAmmoStoreKeys = new List<string>() { chamberStoreKey, magazineStoreKey, revolverStoreKey };

    protected const string oxydContents = "storagebase";

    protected const string configProto = "gunConfig";

    public static readonly TimeSpan forceWaitThreshold = TimeSpan.FromMilliseconds(125);

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
    
    public abstract void doVisUpdate(EntityUid gun);
    

    public void giveTickInterpTime(GunFiremodePrototype prot)
    {
        var giving = _gameTiming.CurTime - prot.lastInterpret;
        // we reset and give 1 tick only
        if (giving / _gameTiming.TickPeriod > 10)
        {
            prot.lastInterpret = _gameTiming.CurTime;
            prot.timeBudget = _gameTiming.TickPeriod;
            return;
        }

        if(giving.Ticks > 0)
            prot.timeBudget += giving;
        Log.Debug($"Ran giveTickTime, was given {(_gameTiming.CurTime - prot.lastInterpret).Milliseconds}ms, total: {prot.timeBudget.Milliseconds}ms");
        prot.lastInterpret = _gameTiming.CurTime;
    }


    [SubscribeLocalEvent]
    public void onModifiersUpdated(Entity<OxydGunComponent> gun, ref ModifiersUpdatedEvent args)
    {
        foreach (var firemode in gun.Comp.InstanciatedFiremodes)
        {
            firemode.ApplyMods(args.mods);
        }
    }

    [SubscribeLocalEvent]
    public void onChargeInit(Entity<OxydChargeComponent> ent, ref ComponentInit args)
    {
        if (!TryComp<BatteryComponent>(ent, out var bat))
        {
            Log.Error($"OxydChargeComponent on {ent} has no BatteryComponent!");
            return;
        }
        ent.Comp.charge = bat.StartingCharge;
    }
    [SubscribeLocalEvent]
    public void onBatteryCharge(Entity<OxydChargeComponent> ent, ref ChargeChangedEvent args)
    {
        // cancel update to battery if client-side and active
        if (_netManager.IsClient && _players.LocalEntity is not null)
        {
            if (_help.GetParentWithComp<OxydGunComponent>(ent.Owner, out var target))
            {
                if (_hands.EnumerateHeld((_players.LocalEntity.Value, null))
                    .ToList()
                    .Contains(target.Value.Owner) && target.Value.Comp.selectedFiremodePrototype.Active)
                    return;
            }
        }
        Log.Debug($"Oxyd battery charge from {ent.Comp.charge} to {args.CurrentCharge}");

        ent.Comp.charge = args.CurrentCharge;
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
    


    public abstract bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter);

    public Vector2 GetBulletInitialMovementDirection(Entity<OxydProjectileComponent> projectile, Entity<OxydGunComponent> gun, CompoundedModifiers mods,  MapCoordinates shootingFrom, MapCoordinates targetPos, EntityUid shooter)
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
        //Log.Debug($"b: {ev.baseInaccuracy.Degrees}, a: {ev.addedInaccuracy.Degrees}");
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
            if (!TryComp<OxydChargeComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (batt.charge < amount)
                continue;

            batt.charge -= amount;
            _battery.UseCharge((slot.ContainedEntity.Value, null), amount);
            used = slot.ContainedEntity;
            return true;

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
            if (!TryComp<OxydChargeComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (batt.charge >= amount)
                return true;
        }
        return false;
    }

    public bool tryGetProviderAmmo(Entity<OxydGunComponent> gun, string key,
        [NotNullWhen(true)] out EntProtoId? projectile,
        [NotNullWhen(true)] out EntityUid? ammo)
    {
        ammo  = null;
        projectile = null;
        var ev = new GunGetAmmoEvent(key);
        RaiseLocalEvent(gun, ref ev);
        if (TerminatingOrDeleted(ev.ammo))
            return false;
        projectile = ev.projectile;
        ammo = ev.ammo;
        return true;
    }

    public bool hasProviderAmmo(Entity<OxydGunComponent> gun, string key)
    {
        var ev = new GunHasAmmoEvent(key);
        RaiseLocalEvent(gun, ref ev);
        return ev.hasAmmo;
    }

    public void afterProviderAmmo(Entity<OxydGunComponent> gun, string key, EntityUid ammo,  EntityUid projectile)
    {
        var ev = new GunAfterUseAmmoEvent(key, ammo, projectile);
        RaiseLocalEvent(gun,ref ev);
    }

    public void applyMods(Entity<OxydProjectileComponent> projectile, CompoundedModifiers mods)
    {
        OxydProjectileApplyDamageComponent? dc = null;
        OxydBulletOnFireRecoilComponent? rc = null;
        TryComp<OxydProjectileApplyDamageComponent>(projectile, out dc);
        TryComp<OxydBulletOnFireRecoilComponent>(projectile, out rc);

        if (mods.damageAdd is not null)
        {
            dc ??= EnsureComp<OxydProjectileApplyDamageComponent>(projectile);
            dc.DamageSpecifier += mods.damageAdd;
        }

        if (mods.damageMult is not null && dc is not null)
        {
            dc.DamageSpecifier = DamageHelpers.multiply(dc.DamageSpecifier, mods.damageMult);
        }

        if (mods.recoilAdd != 0)
        {
            rc ??= EnsureComp<OxydBulletOnFireRecoilComponent>(projectile);
            rc.recoil += mods.recoilAdd;
        }

        if (mods.recoilMult != 1 && rc is not null)
        {
            rc.recoil *= mods.recoilMult;
        }

    }


    public bool getProjectileLoaded(EntityUid shooter, Entity<OxydGunComponent> gun, GunFiremodePrototype firemode,CompoundedModifiers mods,
        [NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp,
        [NotNullWhen(true)] out EntityUid? used)
    {
        outputComp = null;
        used = null;
        if (!tryGetProviderAmmo(gun, firemode.providerId, out var proj, out var ammoEnt))
            return false;
        EntityUid projectile = Spawn(proj.ToString(), MapCoordinates.Nullspace);
        var projectileComp = EnsureComp<OxydProjectileComponent>(projectile);
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        applyMods((projectile, projectileComp), mods);
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
        MapCoordinates targetPos,
        int shots)
    {
        GunFiremodePrototype gunFiremodePrototype = gun.Comp.selectedFiremodePrototype;
        var mods = _mods.getModifiers(gun.Owner);
        AudioParams param = AudioParams.Default;
        param.Volume += mods.soundVolume;
        param.Pitch += mods.soundPitch;
        HashSet<Entity<OxydProjectileComponent>> projectiles = new();
        var sameTickCounter = 0;
        var gbfev = new GunBeforeFireIndividualProjectileEvent()
        {
            simTick = gun.Comp.simulateAsTick
        };
        var gafev =  new GunAfterFireIndividualProjectileEvent()
        {
            simTick = gun.Comp.simulateAsTick
        };
        while (shots-- > 0)
        {
            Log.Debug($"Firing gun ---");
            if (!getProjectileLoaded(shooter, gun, gunFiremodePrototype, mods, out var projectileNullable, out var used))
            {
                continue;
            }
            var shootSound = gunFiremodePrototype.fireSound;
            gbfev.projectile = projectileNullable.Value;
            RaiseLocalEvent(gun.Owner, gbfev);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, gbfev);
            Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
            projectile.Comp.initialMovement *= gunFiremodePrototype.SpeedMultiplier;
            projectile.Comp.initialMovement *= GetBulletInitialMovementDirection(projectile, gun, mods, shootingFrom, targetPos, shooter);
            projectile.Comp.initialPosition = shootingFrom.Offset(projectile.Comp.initialMovement * sameTickCounter * (float)gunFiremodePrototype.spentBudget.TotalSeconds);
            _transformSystem.SetWorldRotationNoLerp(projectile.Owner, projectile.Comp.initialMovement.ToAngle());
            projectile.Comp.aimedPosition = targetPos;
            projectiles.Add(projectile);
            _projectileSystem.queueProjectile(projectile);
            gun.Comp.timesFired++;
            sameTickCounter++;
            gafev.projectile = projectile;
            var filter = Filter.Pvs(gun, _help.getRangeToPvsMultiplier(25f + mods.soundRange));
            if (_netManager.IsServer)
                filter.RemoveWhereAttachedEntity(play => play == shooter);
            _audio.PlayEntity(_audio.ResolveSound(shootSound), filter, gun.Owner, true, param.WithPlayOffset((float)gunFiremodePrototype.spentBudget.TotalSeconds));
            RaiseLocalEvent(gun.Owner, gafev);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, gafev);
            afterProviderAmmo(gun,gunFiremodePrototype.providerId, used.Value, projectile.Owner);
        }

        if (sameTickCounter == 0)
            return projectiles;
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
    [SubscribeLocalEvent]
    public void onGunInitialized(Entity<OxydGunComponent> gun, ref ComponentInit args)
    {
        var providers = AllComps<BaseGunProvider>(gun).ToList();
        foreach (var proto in gun.Comp.firemodes)
        {
            var newFiremode = _prototypeManager.Index<GunFiremodePrototype>(proto).createCopy();
            newFiremode.Initialize();
            var valid = false;
            foreach (var provider in providers)
            {
                if (provider.getKeys().Contains(newFiremode.providerId))
                {
                    valid = true;
                    break;
                }
            }
            if(!valid)
            {
                Log.Debug($"Missing provider for id: {newFiremode.providerId} for firemode prototype: {proto} on gun prototype id: {MetaData(gun).EntityPrototype?.ID}");
                continue;
            }

            gun.Comp.InstanciatedFiremodes.Add(newFiremode);

        }
        gun.Comp.selectedFiremodeIndex = 0;
        _containerSystem.EnsureContainer<Container>(gun, oxydContents);
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
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates, int shotCount)
    {
        return fireGun(shooter, gun, firingCoordinates, targetCoordinates, shotCount);
    }

    public void EnsureActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        Log.Debug($"Updating+++");
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = true;
    }
    public void RemoveActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        Log.Debug($"Updating---");
        //Log.Debug($"Queued for active removal {gun.Owner}");
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = false;
    }


    public bool TryExecuteFiremodeCycle(GunFiremodePrototype firemodePrototype, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        //Log.Debug($"Executing firecycle at {_gameTiming.CurTick}");
        if (gun.Comp.jammed)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            Log.Debug($"Interpret failed: jam");
            return false;
        }

        if (firemodePrototype.timeBudget.Milliseconds <= 0)
        {
            Log.Debug($"Interpret failed: time budget");
            return false;
        }

        if (_netManager.IsServer)
        {
            // wait for network update at this stage
            if (_gameTiming.CurTime - gun.Comp.lastNetMouseUpdate > forceWaitThreshold)
            {
                Log.Debug($"Interpret failed: force wait");
                return true;
            }
        }
        firemodePrototype.lastInterpret = _gameTiming.CurTime;
        firemodePrototype.Active = true;
        while(firemodePrototype.timeBudget.Milliseconds > 0)
        {
            Log.Debug($"step:{firemodePrototype.Effects[firemodePrototype.currentStep]},index:{firemodePrototype.currentStep},time:{firemodePrototype.timeBudget.Milliseconds},tick: {_gameTiming.CurTick}");
            if (!InterpretStep(firemodePrototype, firemodePrototype.Effects[firemodePrototype.currentStep], gun, shooter))
            {
                Log.Debug($"cycle:{firemodePrototype.timeBudget.Milliseconds}");
                break;
            }
            firemodePrototype.currentStep++;
            if (firemodePrototype.currentStep == firemodePrototype.maxSteps)
            {
                firemodePrototype.currentStep = 0;
                ResetEffs(firemodePrototype);
            }
        }

        if (firemodePrototype.currentStep == firemodePrototype.maxSteps)
        {
            firemodePrototype.currentStep = 0;
            ResetEffs(firemodePrototype);
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
