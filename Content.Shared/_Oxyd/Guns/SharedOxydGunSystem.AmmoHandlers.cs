
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;

namespace Content.Shared._Oxyd.OxydGunSystem;

public abstract partial class SharedOxydGunSystem
{
    [Dependency] private OxydPredContainerSystem predconts = default!;
    
    [SubscribeLocalEvent]
    private void InitHandler(EntityUid id, OxydPredictedGunStorageComponent comp, ComponentInit ev)
    {
        foreach (var cont in comp.storeKeys)
        {
            predconts.CreateContainer(id, cont, null);
        }
    }

    [SubscribeLocalEvent([typeof(ItemSlotsSystem)])]
    private void OnInt(EntityUid id, OxydPredictedGunStorageComponent comp, InteractUsingEvent ev)
    {
        if (ev.Handled)
            return;
        if (HasComp<BundleComponent>(ev.Used))
            return;
        ev.Handled = predconts.insertEntity(id, comp.storeKeys[0], ev.Used);
    }
    
}