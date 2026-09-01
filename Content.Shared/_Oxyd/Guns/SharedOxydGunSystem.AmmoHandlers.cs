
using System.Linq;
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
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
        if (!HasComp<OxydBulletComponent>(args.Used))
            return;
        if (TerminatingOrDeleted(args.Used))
            return;
        var ev = new GunTryLoadAmmoEvent(args.Used, true);
        RaiseLocalEvent(gun,ref ev);
        args.Handled = ev.handled;
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
            values.store = $"{key}-{chamberStoreKey}";
            conts.CreateContainer(uid, values.store, values.capacity);
        }
    }
    
    public void InitializeMappings(EntityUid uid, Dictionary<string, MagazineData> data)
    {
        foreach (var (key, values) in data)
        {
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
        
        state.ignore = FindActionActor((gun, Comp<OxydGunComponent>(gun)));
    }

    [SubscribeLocalEvent]
    public void RevolverApplyState(Entity<OxydRevolvingChamberComponent> gun, ref ComponentHandleState args)
    {
        if (_help.shouldIgnoreState(args.Current))
            return;
        if (args.Current is RevolverDataState state)
        {
            foreach (var (key, data) in state.providers)
            {
                gun.Comp.providers[key] = data.data;
                Array.Resize(ref data.data.loaded, data.data.capacity);
                Array.Fill(data.data.loaded, EntityUid.Invalid);
                for(var i = 0; i < data.loadedNet.Length; i++)
                {
                    var ent = GetEntity(data.loadedNet[i]);
                    if (TerminatingOrDeleted(ent))
                        continue;
                    data.data.loaded[i] = ent;
                }
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
                provider.capacity += args.mods.gunCapacityAdd;
                Array.Resize(ref provider.loaded, provider.capacity);
                Array.Fill(provider.loaded, EntityUid.Invalid);
            }
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
        args.hasAmmo = !TerminatingOrDeleted(data.loaded[data.index]);
    }

    [SubscribeLocalEvent]
    public void RevolverGetAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunGetAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        if (!conts.GetContainer(gun, data.store, out var cont))
            return;
        EntityUid target = EntityUid.Invalid;
        if (_gameTiming.IsFirstTimePredicted)
        {
            if (!cont.contained.TryGetValue(data.index, out target))
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
    public void RevolverAfterUse(Entity<OxydRevolvingChamberComponent> gun, ref GunAfterUseAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        data.index++;
        if(data.index >= data.capacity)
            data.index = 0;
    }
}