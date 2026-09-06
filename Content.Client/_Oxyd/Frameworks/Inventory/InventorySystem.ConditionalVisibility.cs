using Content.Shared.Inventory;
using Robust.Client.Timing;
using Robust.Shared.Timing;

namespace Content.Client.Inventory;


public sealed class SlotDataAction
{
    public required ClientInventorySystem.SlotData data;
    public EntityUid componentOwner;
}
public sealed partial class ClientInventorySystem
{
    [Dependency] private IGameTiming _timing = default!;
    public void OxydInit()
    {
        EntitySlotUpdate -= CheckForDependencyUpdates;
        EntitySlotUpdate += CheckForDependencyUpdates;
    }

    public void CheckForDependencyUpdates(SlotDataAction args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;
        if(!TryComp<InventoryComponent>(args.componentOwner, out var inv)) 
            return;
        var e = (args.componentOwner, inv);
        var en = GetSlotEnumerator(e, SlotFlags.DEPENDANTRENDER);
        while (en.MoveNext(out _, out var def))
        {
            if (def.DependsOn == args.data.SlotName)
            {
                ReloadInventory();
                return;
            }
        }
    }
}