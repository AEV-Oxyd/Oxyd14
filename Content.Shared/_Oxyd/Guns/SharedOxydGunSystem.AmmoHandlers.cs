
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
            conts.CreateContainer(uid, values.store, values.roundLimit);
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
            if (!conts.insertEntity(gun, provider.store, args.ammo, null, args.prediction, provider.ind))
                continue;
            args.handled = true;
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
        args.hasAmmo = cont.contained.TryGetValue(data.index, out _);
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
            if (!cont.contained.TryGetValue(data.index++, out target))
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
}