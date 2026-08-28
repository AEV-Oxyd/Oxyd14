
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Whitelist;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem
{
    [Dependency] protected EntityWhitelistSystem whitelist = default!;
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
            values.store = $"{key}-{revolverStoreKey}";
            conts.CreateContainer(uid, values.store, values.capacity);
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