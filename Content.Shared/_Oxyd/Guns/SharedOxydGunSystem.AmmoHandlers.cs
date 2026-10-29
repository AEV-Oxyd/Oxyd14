using System.Linq;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem
{
    [Dependency] protected EntityWhitelistSystem whitelist = default!;

    [SubscribeLocalEvent]
    public void OnAttack(Entity<OxydGunComponent> gun, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        if (TerminatingOrDeleted(args.Used))
            return;
        var ev = new GunTryLoadAmmoEvent(args.Used, true);
        RaiseLocalEvent(gun,ref ev);
        args.Handled = ev.handled;
    }

    [SubscribeLocalEvent]
    public void MagazineCompInit(Entity<OxydMagazineComponent> ent, ref ComponentInit args)
    {
        if (string.IsNullOrEmpty(ent.Comp.storeKey))
            ent.Comp.storeKey = oxydContents;
        conts.CreateContainer(ent, ent.Comp.storeKey, (int)ent.Comp.maxBullets);
    }

    [SubscribeLocalEvent]
    public void OnMagazineInteractUsing(Entity<OxydMagazineComponent> mag, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        if (TerminatingOrDeleted(args.Used))
            return;
        if (mag.Comp.whitelist.Components is not null || mag.Comp.whitelist.Tags is not null)
        {
            if (!whitelist.IsWhitelistPass(mag.Comp.whitelist, args.Used))
                return;
        }
        else if (!HasComp<OxydBulletComponent>(args.Used))
        {
            return;
        }

        var storeKey = string.IsNullOrEmpty(mag.Comp.storeKey) ? oxydContents : mag.Comp.storeKey;
        var bullet = args.Used;
        args.Handled = conts.insertEntity(mag.Owner, storeKey, ref bullet, null, true);
    }

    [SubscribeLocalEvent]
    public void OnMagazineUseInHand(Entity<OxydMagazineComponent> mag, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        var storeKey = string.IsNullOrEmpty(mag.Comp.storeKey) ? oxydContents : mag.Comp.storeKey;
        if (!conts.GetContainer(mag.Owner, storeKey, out var cont))
            return;
        EntityUid target = EntityUid.Invalid;
        if (_gameTiming.IsFirstTimePredicted)
        {
            if (cont.contained.Count == 0)
                return;
            target = cont.contained[^1];
        }
        if (conts.removeEntity(mag.Owner, cont, ref target, null, null, true))
        {
            _hands.TryPickupAnyHand(args.User, target);
            args.Handled = true;
        }
    }
    
    [SubscribeLocalEvent]
    public void ChamberInit(Entity<OxydChamberComponent> ent, ref ComponentInit args) => InitializeMappings(ent, ent.Comp.providers);
    [SubscribeLocalEvent]
    public void MagazineInit(Entity<OxydMagazineChamberComponent> ent, ref ComponentInit args) => InitializeMappings(ent, ent.Comp.providers);
    [SubscribeLocalEvent]
    public void RevolverInit(Entity<OxydRevolvingChamberComponent> ent, ref ComponentInit args) => InitializeMappings(ent, ent.Comp.providers);

    public void InitializeMappings(EntityUid uid, Dictionary<string, ChamberData> data)
    {
        foreach (var (key, values) in data)
        {
            if (values.capacity == 0)
                values.capacity = values.basecapacity;
            values.store = $"{key}-{chamberStoreKey}";
            conts.CreateContainer(uid, values.store, values.capacity);
        }
    }
    
    public void InitializeMappings(EntityUid uid, Dictionary<string, MagazineData> data)
    {
        foreach (var (key, values) in data)
        {
            if (values.capacity == 0)
                values.capacity = values.basecapacity;
            values.store = $"{key}-{magazineStoreKey}-{chamberStoreKey}";
            conts.CreateContainer(uid, values.store, values.capacity);
            values.magstore = $"{key}-{magazineStoreKey}";
            conts.CreateContainer(uid, values.magstore, 1);
        }
    }
    
    // revolver
    
    public void InitializeMappings(EntityUid uid, Dictionary<string, RevolverData> data)
    {
        foreach (var (key, values) in data)
        {
            if (values.capacity == 0)
                values.capacity = values.basecapacity;
            values.store = $"{key}-{revolverStoreKey}";
            conts.CreateContainer(uid, values.store, values.capacity);
            Array.Resize(ref values.loaded, values.capacity);
            Array.Fill(values.loaded, EntityUid.Invalid);
        }
    }

    public NetEntity FindActionActor(Entity<OxydGunComponent> gun)
    {
        if(!gun.Comp.selectedFiremodePrototype.Active)
            return NetEntity.Invalid;
        foreach (var data in checkActive)
        {
            // should probably store a reference of active shooter on the firemode? SPCR 2026
            if (data.firemode != gun.Comp.selectedFiremodePrototype)
                continue;
            if (data.shooter is null)
                continue;
            return GetNetEntity(data.shooter.Value);
        }

        return NetEntity.Invalid;

    }

    [SubscribeLocalEvent]
    public void RevolverGetState(Entity<OxydRevolvingChamberComponent> gun, ref ComponentGetState args)
    {
        if (_netManager.IsClient)
            return;
        var dict = new Dictionary<string, RevolverNetworkData>();
        foreach (var (key, data) in gun.Comp.providers)
        {
            dict[key] = new RevolverNetworkData()
            {
                data = data,
                loadedNet = data.loaded.Select(ent => GetNetEntity(ent)).ToArray()
            };
        }
        var state = new RevolverDataState(){ providers = dict };
        args.State = state;
        Log.Info($"Acquired revolver state, {state.providers.Count} providers, {state.providers.Sum(x => x.Value.loadedNet.Length)} loaded");
        
        state.ignore = FindActionActor((gun, Comp<OxydGunComponent>(gun)));
    }

    [SubscribeLocalEvent]
    public void RevolverApplyState(Entity<OxydRevolvingChamberComponent> gun, ref ComponentHandleState args)
    {
        if (_help.shouldIgnoreState(args.Current))
            return;
        // C# Hellbug where doing gun.comp.providers[key] apparently never materialized any change!!
        var dictRef = gun.Comp.providers;
        if (args.Current is RevolverDataState state)
        {
            foreach (var (key, data) in state.providers)
            {
                dictRef[key] = data.data;
                data.data.loaded = new EntityUid[data.data.capacity];
                Array.Fill(data.data.loaded, EntityUid.Invalid);
                for(var i = 0; i < data.loadedNet.Length; i++)
                {
                    var ent = GetEntity(data.loadedNet[i]);
                    if (TerminatingOrDeleted(ent))
                        continue;
                    data.data.loaded[i] = ent;
                }
                Log.Warning($"Applied revolver state with {data.data.capacity} , key is now {gun.Comp.providers[key].capacity} for {key}");
            }
        }
    }
    
    [SubscribeLocalEvent]
    public void RevolverApplyMods(Entity<OxydRevolvingChamberComponent> gun, ref ModifiersUpdatedEvent args)
    {
        if (_netManager.IsClient)
            return;
        if (args.mods.gunCapacityAdd > 0)
        {
            foreach (var provider in gun.Comp.providers.Values)
            {
                provider.capacity = provider.basecapacity + args.mods.gunCapacityAdd;
                conts.SetContainerCapacity(gun, provider.store, provider.capacity);
                Array.Resize(ref provider.loaded, provider.capacity);
                Array.Fill(provider.loaded, EntityUid.Invalid);
            }
            Log.Warning($"Applying mods on revolver");
            Dirty(gun);
        }
        
    }

    [SubscribeLocalEvent]
    public void ChamberApplyMods(Entity<OxydChamberComponent> gun, ref ModifiersUpdatedEvent args)
    {
        if (_netManager.IsClient)
            return;
        if (args.mods.gunCapacityAdd > 0)
        {
            foreach (var provider in gun.Comp.providers.Values)
            {
                provider.capacity = provider.basecapacity + args.mods.gunCapacityAdd;
                conts.SetContainerCapacity(gun, provider.store, provider.capacity);
            }
            Dirty(gun);
        }
    }

    [SubscribeLocalEvent]
    public void MagazineApplyMods(Entity<OxydMagazineChamberComponent> gun, ref ModifiersUpdatedEvent args)
    {
        if (_netManager.IsClient)
            return;
        if (args.mods.gunCapacityAdd > 0)
        {
            foreach (var provider in gun.Comp.providers.Values)
            {
                provider.capacity = provider.basecapacity + args.mods.gunCapacityAdd;
                conts.SetContainerCapacity(gun, provider.store, provider.capacity);
            }
            Dirty(gun);
        }
    }
    
    [SubscribeLocalEvent]
    public void RevolverContInsert(Entity<OxydRevolvingChamberComponent> gun, ref PredContInserted args)
    {
        if (!args.realChange)
            return;
        if (!args.container.key.Contains(revolverStoreKey))
            return;
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (data.store != args.container.key)
                continue;
            var index = data.index;
            var iterationLimit = data.capacity;
            var iters = 0;
            while (iters++ < iterationLimit )
            {
                if (TerminatingOrDeleted(data.loaded[index]))
                {
                    data.loaded[index] = args.uid;
                    break;
                }
                index++;
                if (index >= data.capacity)
                    index = 0;
            }

            break;
        }
    }

    [SubscribeLocalEvent]
    public void RevolverContRemove(Entity<OxydRevolvingChamberComponent> gun, ref PredContRemoved args)
    {
        if (!args.realChange)
            return;
        if (!args.container.key.Contains(revolverStoreKey))
            return;
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (data.store != args.container.key)
                continue;
            var index = data.loaded.IndexOf(args.uid);
            if (index == -1)
                break;
            data.loaded[index] = EntityUid.Invalid;
            break;
        }
    }
    
    [SubscribeLocalEvent]
    public void RevolverLoadAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunTryLoadAmmoEvent args)
    {
        if (args.handled)
            return;
        foreach (var provider in gun.Comp.providers.Values)
        {
            if (!whitelist.IsWhitelistPass(provider.whitelist, args.ammo))
                continue;
            var ammoCopy = args.ammo;
            args.handled = conts.insertEntity(gun.Owner, provider.store, ref ammoCopy, null, args.prediction);
            Log.Debug($"Checked provider, returned {args.handled}");
            if(args.handled)
                return;
        }
    }
    
    [SubscribeLocalEvent]
    public void RevolverHasAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunHasAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var cont))
            return;
        args.hasAmmo = cont.contained.Count != 0;
    }

    [SubscribeLocalEvent]
    public void RevolverGetAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunTryGetAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var cont))
            return;
        EntityUid target = EntityUid.Invalid;
        if (_gameTiming.IsFirstTimePredicted)
        {
            var o = data.index++;
            if(data.index >= data.capacity)
                data.index = 0;
            Log.Warning($"Index now {data.index}, {o}");
            if (!data.loaded.TryGetValue(o, out target))
                return;
        }
        else if (!args.prediction)
            return;

        conts.removeEntity(gun, cont, ref target, null, null, args.prediction);
        if (!TryComp<OxydBulletComponent>(target, out var bullet))
            return;
        args.ammo = target;
        args.projectile = bullet.projectileEntity;
    }

    [SubscribeLocalEvent]
    public void ChamberLoadAmmo(Entity<OxydChamberComponent> gun, ref GunTryLoadAmmoEvent args)
    {
        if (args.handled)
            return;
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (!conts.GetContainer(gun, data.store, out var cont))
                continue;
            if (!whitelist.IsWhitelistPass(data.whitelist, args.ammo))
                continue;
            var ammoR = args.ammo;
            int? targetIndex = data.pushback ? 0 : null;
            args.handled = conts.insertEntity(gun, cont, ref ammoR, null, args.prediction, targetIndex);
            if (args.handled)
                return;
        }
    }

    [SubscribeLocalEvent]
    public void ChamberHasAmmo(Entity<OxydChamberComponent> gun, ref GunHasAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var cont))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp)) 
            return;
        args.hasAmmo = cont.contained.Any();
    }

    [SubscribeLocalEvent]
    public void ChamberGetAmmo(Entity<OxydChamberComponent> gun, ref GunTryGetAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var cont))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (FH.hasAny(data.operatingMode, COM.Boltable) && !gcomp.bolted)
            return;
        if (FH.hasAny(data.operatingMode, COM.Pumpable) && !gcomp.pumped)
            return;
        EntityUid target = EntityUid.Invalid;
        if (_gameTiming.IsFirstTimePredicted)
        {
            if (cont.contained.Any())
                target = cont.contained.First();
            else
                return;
        }
        else if (!args.prediction)
            return;
        conts.removeEntity(gun, cont, ref target, null, null, args.prediction);
        if (!TryComp<OxydBulletComponent>(target, out var bullet))
            return;
        gcomp.bolted = false;
        gcomp.pumped = false;
        args.ammo = target;
        args.projectile = bullet.projectileEntity;
    }

    [SubscribeLocalEvent]
    public void ChamberAfterAmmo(Entity<OxydChamberComponent> gun, ref GunAfterUseAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (FH.hasAll(data.operatingMode, COM.Boltable | COM.BoltClosedAutoload))
            gcomp.bolted = true;
    }

    // Magazine Chamber

    public bool TryGetLoadedMagazine(EntityUid gun, MagazineData data, out Entity<OxydMagazineComponent> mag, out OxydContainer magCont)
    {
        mag = default;
        magCont = default!;
        if (!conts.GetContainer(gun, data.magstore, out var magStoreCont))
            return false;
        if (!magStoreCont.contained.Any())
            return false;
        var magUid = magStoreCont.contained.First();
        if (!TryComp<OxydMagazineComponent>(magUid, out var magComp))
            return false;
        var storeKey = string.IsNullOrEmpty(magComp.storeKey) ? oxydContents : magComp.storeKey;
        if (!conts.GetContainer(magUid, storeKey, out var foundMagCont))
            return false;
        mag = (magUid, magComp);
        magCont = foundMagCont;
        return true;
    }

    public bool ShouldAutoChamber(EntityUid gun, MagazineData data)
    {
        if (FH.hasAny(data.operatingMode, COM.Auto))
            return true;
        if (TryComp<OxydGunComponent>(gun, out var gcomp) &&
            FH.hasAll(data.operatingMode, COM.Boltable | COM.BoltClosedAutoload) &&
            gcomp.bolted)
            return true;
        return false;
    }

    /// <summary>
    /// Cycles rounds from the inserted magazine into the gun's chamber container up to the chamber's capacity.
    /// Uses OxydPredContainerSystem to move bullets from the magazine's predictive container into the chamber's predictive container.
    /// </summary>
    public bool TryChamberFromMagazine(EntityUid gun, MagazineData data, bool handlePredict = false)
    {
        if (!conts.GetContainer(gun, data.store, out var chamberCont))
            return false;

        var chamberedAny = false;
        if (_gameTiming.IsFirstTimePredicted)
        {
            if (!TryGetLoadedMagazine(gun, data, out _, out var magCont))
                return false;

            while ((chamberCont.capacityLimit is null || chamberCont.contained.Count < chamberCont.capacityLimit) &&
                   magCont.contained.Count > 0)
            {
                var bullet = magCont.contained[^1];
                int? targetIndex = data.pushback ? 0 : null;
                if (!conts.insertEntity(gun, chamberCont, ref bullet, null, handlePredict, targetIndex))
                    break;
                chamberedAny = true;
            }
        }
        else if (handlePredict)
        {
            if (!chamberCont.predictions.Get(_gameTiming.CurTick.Value, out var predQueue) || predQueue.Count == 0)
                return false;
            var count = predQueue.Count;
            for (var i = 0; i < count; i++)
            {
                EntityUid bullet = EntityUid.Invalid;
                int? targetIndex = data.pushback ? 0 : null;
                if (conts.insertEntity(gun, chamberCont, ref bullet, null, true, targetIndex))
                    chamberedAny = true;
            }
        }

        return chamberedAny;
    }

    [SubscribeLocalEvent]
    public void MagazineLoadAmmo(Entity<OxydMagazineChamberComponent> gun, ref GunTryLoadAmmoEvent args)
    {
        if (args.handled)
            return;

        var isMag = HasComp<OxydMagazineComponent>(args.ammo);
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (isMag)
            {
                if (!conts.GetContainer(gun, data.magstore, out var magStoreCont))
                    continue;
                if (data.magwhitelist.Components is not null || data.magwhitelist.Tags is not null)
                {
                    if (!whitelist.IsWhitelistPass(data.magwhitelist, args.ammo))
                        continue;
                }
                var ammoR = args.ammo;
                args.handled = conts.insertEntity(gun, magStoreCont, ref ammoR, null, args.prediction);
                if (args.handled)
                {
                    if (ShouldAutoChamber(gun, data))
                        TryChamberFromMagazine(gun, data, args.prediction);
                    doVisUpdate(gun.Owner);
                    return;
                }
            }
            else
            {
                if (!conts.GetContainer(gun, data.store, out var chamberCont))
                    continue;
                if (data.whitelist.Components is not null || data.whitelist.Tags is not null)
                {
                    if (!whitelist.IsWhitelistPass(data.whitelist, args.ammo))
                        continue;
                }
                else if (!HasComp<OxydBulletComponent>(args.ammo))
                {
                    continue;
                }
                var ammoR = args.ammo;
                int? targetIndex = data.pushback ? 0 : null;
                args.handled = conts.insertEntity(gun, chamberCont, ref ammoR, null, args.prediction, targetIndex);
                if (args.handled)
                    return;
            }
        }
    }

    [SubscribeLocalEvent]
    public void MagazineHasAmmo(Entity<OxydMagazineChamberComponent> gun, ref GunHasAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var chamberCont))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (FH.hasAny(data.operatingMode, COM.Boltable) && !gcomp.bolted)
            return;
        if (FH.hasAny(data.operatingMode, COM.Pumpable) && !gcomp.pumped)
            return;

        if (!chamberCont.contained.Any() && ShouldAutoChamber(gun, data) && _gameTiming.IsFirstTimePredicted)
        {
            TryChamberFromMagazine(gun, data, false);
        }

        args.hasAmmo = chamberCont.contained.Any();
    }

    [SubscribeLocalEvent]
    public void MagazineGetAmmo(Entity<OxydMagazineChamberComponent> gun, ref GunTryGetAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var chamberCont))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (FH.hasAny(data.operatingMode, COM.Boltable) && !gcomp.bolted)
            return;
        if (FH.hasAny(data.operatingMode, COM.Pumpable) && !gcomp.pumped)
            return;

        EntityUid target = EntityUid.Invalid;
        if (_gameTiming.IsFirstTimePredicted)
        {
            if (!chamberCont.contained.Any() && ShouldAutoChamber(gun, data))
            {
                TryChamberFromMagazine(gun, data, false);
            }

            if (chamberCont.contained.Any())
                target = chamberCont.contained.First();
            else
                return;
        }
        else if (!args.prediction)
            return;

        if (!conts.removeEntity(gun, chamberCont, ref target, null, null, args.prediction))
            return;
        if (!TryComp<OxydBulletComponent>(target, out var bullet))
            return;
        if (!FH.hasAny(data.operatingMode, COM.Auto))
            gcomp.bolted = false;
        gcomp.pumped = false;
        args.ammo = target;
        args.projectile = bullet.projectileEntity;
    }

    [SubscribeLocalEvent]
    public void MagazineAfterAmmo(Entity<OxydMagazineChamberComponent> gun, ref GunAfterUseAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        if (FH.hasAny(data.operatingMode, COM.Auto) ||
            FH.hasAll(data.operatingMode, COM.Boltable | COM.BoltClosedAutoload))
        {
            gcomp.bolted = true;
            TryChamberFromMagazine(gun, data, false);
        }
    }

    public bool TryEjectMagazine(Entity<OxydMagazineChamberComponent> gun, EntityUid user, bool handlePredict = true)
    {
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (!conts.GetContainer(gun, data.magstore, out var magStoreCont))
                continue;
            EntityUid target = EntityUid.Invalid;
            if (_gameTiming.IsFirstTimePredicted)
            {
                if (!magStoreCont.contained.Any())
                    continue;
                target = magStoreCont.contained.First();
            }

            if (conts.removeEntity(gun, magStoreCont, ref target, null, null, handlePredict))
            {
                _hands.TryPickupAnyHand(user, target);
                doVisUpdate(gun.Owner);
                return true;
            }
        }

        return false;
    }

    [SubscribeLocalEvent]
    public void MagazineUseInHand(Entity<OxydMagazineChamberComponent> gun, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = TryEjectMagazine(gun, args.User, true);
    }

    [SubscribeLocalEvent]
    public void MagazineAltVerbs(Entity<OxydMagazineChamberComponent> gun, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var hasMag = false;
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (conts.GetContainer(gun, data.magstore, out var magStoreCont) && magStoreCont.contained.Any())
            {
                hasMag = true;
                break;
            }
        }

        if (!hasMag)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryEjectMagazine(gun, user, true),
            Text = "Eject Magazine",
        });
    }

    [SubscribeLocalEvent]
    public void MagazineContInsert(Entity<OxydMagazineChamberComponent> gun, ref PredContInserted args)
    {
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (args.container.key == data.magstore)
            {
                doVisUpdate(gun.Owner);
                break;
            }
        }
    }

    [SubscribeLocalEvent]
    public void MagazineContRemove(Entity<OxydMagazineChamberComponent> gun, ref PredContRemoved args)
    {
        foreach (var (_, data) in gun.Comp.providers)
        {
            if (args.container.key == data.magstore)
            {
                doVisUpdate(gun.Owner);
                break;
            }
        }
    }

    [SubscribeLocalEvent]
    public void MagazineContReset(Entity<OxydMagazineChamberComponent> gun, ref PredContStateReset args)
    {
        doVisUpdate(gun.Owner);
    }
}
