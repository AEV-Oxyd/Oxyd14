using Content.Shared.Inventory;

namespace Content.Shared.Inventory;

/// <summary>
/// This handles...
/// </summary>
public partial class InventorySystem
{
    public bool HasDependenciesFulfilled(EntityUid target, SlotDefinition slotDefinition, InventoryComponent? inventory = null)
    {
        if(!Resolve(target, ref inventory, false))
            return false;
        if (slotDefinition.DependsOn != null)
        {
            if (!TryGetSlotEntity(target, slotDefinition.DependsOn, out EntityUid? slotEntity, inventory))
                return false;

            if (slotDefinition.DependsOnComponents is { } componentRegistry)
            {
                foreach (var (_, entry) in componentRegistry)
                {
                    if (!HasComp(slotEntity, entry.Component.GetType()))
                        return false;
                }
            }
        }
        return true;
    }
}