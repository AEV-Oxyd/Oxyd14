using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Shared.PDA;

public abstract class SharedPdaSystem : EntitySystem
{
    [Dependency] protected ItemSlotsSystem ItemSlotsSystem = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] private SharedJobStatusSystem _jobStatus = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PdaComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PdaComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<PdaComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<PdaComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<PdaComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    protected virtual void OnComponentInit(EntityUid uid, PdaComponent pda, ComponentInit args)
    {
        if (pda.IdCard != null)
            pda.IdSlot.StartingItem = pda.IdCard;

        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaIdSlotId, pda.IdSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPenSlotId, pda.PenSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaPaiSlotId, pda.PaiSlot);
        ItemSlotsSystem.AddItemSlot(uid, PdaComponent.PdaDiskSlotId, pda.DiskSlot);

        UpdatePdaAppearance(uid, pda);
    }

    private void OnComponentRemove(EntityUid uid, PdaComponent pda, ComponentRemove args)
    {
        ItemSlotsSystem.RemoveItemSlot(uid, pda.IdSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.PenSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.PaiSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, pda.DiskSlot);
    }

    protected virtual void OnItemInserted(EntityUid uid, PdaComponent pda, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == PdaComponent.PdaIdSlotId)
            pda.ContainedId = args.Entity;
        else if (args.Container.ID == PdaComponent.PdaDiskSlotId)
            pda.ContainedDisk = args.Entity;

        UpdatePdaAppearance(uid, pda);
        _jobStatus.UpdateStatus(Transform(uid).ParentUid);
    }

    protected virtual void OnItemRemoved(EntityUid uid, PdaComponent pda, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == PdaComponent.PdaIdSlotId)
            pda.ContainedId = null;
        else if (args.Container.ID == PdaComponent.PdaDiskSlotId)
            pda.ContainedDisk = null;

        UpdatePdaAppearance(uid, pda);
        _jobStatus.UpdateStatus(Transform(uid).ParentUid);
    }

    private void OnGetAdditionalAccess(EntityUid uid, PdaComponent component, ref GetAdditionalAccessEvent args)
    {
        if ((component.IdSlot.Item ?? component.ContainedId) is { Valid: true } id)
            args.Entities.Add(id);
    }

    protected void UpdatePdaAppearance(EntityUid uid, PdaComponent pda)
    {
        Appearance.SetData(uid, PdaVisuals.IdCardInserted, (pda.IdSlot.Item ?? pda.ContainedId) != null);
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
