using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Shared.PDA;

public abstract class SharedPdaSystem : EntitySystem
{
    [Dependency] protected ItemSlotsSystem ItemSlotsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PdaComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PdaComponent, ComponentRemove>(OnComponentRemove);
    }

    protected virtual void OnComponentInit(EntityUid uid, PdaComponent pda, ComponentInit args)
    {
        if (pda.IdCard != null)
            pda.IdSlot.StartingItem = pda.IdCard;

        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaIdSlotId, pda.IdSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPenSlotId, pda.PenSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPaiSlotId, pda.PaiSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaDiskSlotId, pda.DiskSlot);
    }

    private void OnComponentRemove(EntityUid uid, PdaComponent pda, ComponentRemove args)
    {
        ItemSlotsSystem.RemoveItemSlot(uid, pda.IdSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.PenSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.PaiSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.DiskSlot);
    }

    public void SetOwner(EntityUid uid, PdaComponent pda, EntityUid idCard, string? ownerName)
    {
        pda.OwnerName = ownerName;
        Dirty(uid, pda);
        NotifyHardwareStateChanged(uid, pda);
    }

    public virtual void NotifyHardwareStateChanged(EntityUid uid, PdaComponent? pda = null)
    {
    }
}
