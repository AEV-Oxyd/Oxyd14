
using Content.Client._Oxyd.UI;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Storage;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Inventory;

public sealed partial class ClientInventorySystem
{
    private OxTagController tagger = default!;
    private Dictionary<EntityUid, QuickInventoryStorage> quickStorage = new();

    private void OxydInitQuickStorage()
    {
        tagger = _ui.GetUIController<OxTagController>();
        
    }

    public void UpdateUI(Entity<StorageComponent> entity)
    {
        if(quickStorage.TryGetValue(entity.Owner, out var quick))
            quick.UpdateContained();
    }
    
    [SubscribeLocalEvent]
    public void OnItemInsert(EntityUid uid, InventoryComponent component, DidEquipEvent args)
    {
        if(!TryComp<StorageComponent>(args.Equipment, out var storage))
            return;
        if (quickStorage.TryGetValue(args.Equipment, out var quick))
        {
            quick.UpdateContained();
        }
        else
        {
            var n = new QuickInventoryStorage();
            n.UpdateContainer((args.Equipment, storage));
            quickStorage[args.Equipment] = n;
            foreach (var ctrl in tagger.getControls("quickStorage"))
            {
                ctrl.AddChild(n);
            }
        }
        
    }

    [SubscribeLocalEvent]
    public void OnItemRemove(EntityUid uid, InventoryComponent component, DidUnequipEvent args)
    {
        if(!TryComp<StorageComponent>(args.Equipment, out var storage))
            return;
        if (quickStorage.TryGetValue(args.Equipment, out var quick))
        {
            quick.Orphan();
            quickStorage.Remove(args.Equipment);
        }
        
    }
}
