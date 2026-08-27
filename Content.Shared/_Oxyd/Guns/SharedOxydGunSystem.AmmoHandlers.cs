
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem
{
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
    
    public void InitializeMappings(EntityUid uid, Dictionary<string, RevolverData> data)
    {
        foreach (var (key, values) in data)
        {
            values.store = $"{key}-{revolverStoreKey}";
            conts.CreateContainer(uid, values.store, values.roundLimit);
        }
    }
    // revolver
    

    public void RevolverHasAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunHasAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
        
    }
    public void RevolverGetAmmo(Entity<OxydRevolvingChamberComponent> gun, ref GunGetAmmoEvent args)
    {
        if (!gun.Comp.providers.TryGetValue(args.providerId, out var data))
            return;
    }
}