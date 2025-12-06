using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Storage.Components;

/// <summary>
/// A component that stores items without automatic network updates.
/// Updates only occur when Dirty() is explicitly called server-side.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ManualItemStorageComponent : Component
{
    /// <summary>
    /// The container ID for the stored items.
    /// </summary>
    public const string ContainerId = "manual_item_storage";

    /// <summary>
    /// The container holding the stored items.
    /// </summary>
    [DataField]
    public ContainerSlot ItemContainer = default!;

    /// <summary>
    /// The currently stored item entity, if any.
    /// This is networked only when Dirty() is called.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? StoredItem;

    /// <summary>
    /// Maximum size of item that can be stored.
    /// </summary>
    [DataField]
    public ProtoId<ItemSizePrototype>? MaxItemSize = "Normal";

    /// <summary>
    /// Whether the storage accepts items currently.
    /// </summary>
    [DataField]
    public bool CanInsert = true;

    /// <summary>
    /// Whitelist for items that can be stored.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Blacklist for items that cannot be stored.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Popup message shown when successfully inserting an item.
    /// </summary>
    [DataField]
    public LocId? InsertSuccessPopup;

    /// <summary>
    /// Popup message shown when failing to insert an item.
    /// </summary>
    [DataField]
    public LocId? InsertFailPopup;

    /// <summary>
    /// Popup message shown when the storage is full.
    /// </summary>
    [DataField]
    public LocId? StorageFullPopup;
}

/// <summary>
/// System for managing manual item storage.
/// </summary>
public sealed class ManualItemStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ManualItemStorageComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ManualItemStorageComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<ManualItemStorageComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<ManualItemStorageComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ManualItemStorageComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnComponentInit(EntityUid uid, ManualItemStorageComponent component, ComponentInit args)
    {
        component.ItemContainer = _container.EnsureContainer<ContainerSlot>(uid, ManualItemStorageComponent.ContainerId);
    }

    private void OnItemInserted(EntityUid uid, ManualItemStorageComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != component.ItemContainer.ID)
            return;

        component.StoredItem = args.Entity;
        // Note: Dirty() must be called manually by the code that inserts the item
    }

    private void OnItemRemoved(EntityUid uid, ManualItemStorageComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != component.ItemContainer.ID)
            return;

        component.StoredItem = null;
        // Note: Dirty() must be called manually by the code that removes the item
    }

    private void OnInteractUsing(EntityUid uid, ManualItemStorageComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryInsertItemInHand(uid, args.User, component))
            return;

        args.Handled = true;
    }

    private void OnInteractHand(EntityUid uid, ManualItemStorageComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryRemoveItemToHand(uid, args.User, component))
            return;

        args.Handled = true;
    }

    /// <summary>
    /// Attempts to insert an item into the storage.
    /// Remember to call Dirty(uid, component) after this if you want to network the change.
    /// </summary>
    public bool TryInsertItem(EntityUid uid, EntityUid item, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!component.CanInsert || component.ItemContainer.ContainedEntity != null)
            return false;

        // Check whitelist/blacklist
        if (component.Whitelist != null && !_whitelist.IsValid(component.Whitelist, item))
            return false;

        if (component.Blacklist != null && _whitelist.IsValid(component.Blacklist, item))
            return false;

        // Check item size
        if (component.MaxItemSize != null && TryComp<ItemComponent>(item, out var itemComp))
        {
            if (_item.GetItemSizeWeight(component.MaxItemSize.Value) < _item.GetItemSizeWeight(itemComp.Size))
                return false;
        }

        return _container.Insert(item, component.ItemContainer);
    }

    /// <summary>
    /// Attempts to remove the stored item.
    /// Remember to call Dirty(uid, component) after this if you want to network the change.
    /// </summary>
    public bool TryRemoveItem(EntityUid uid, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (component.ItemContainer.ContainedEntity == null)
            return false;

        return _container.Remove(component.ItemContainer.ContainedEntity.Value, component.ItemContainer);
    }

    /// <summary>
    /// Gets the currently stored item, if any.
    /// </summary>
    public EntityUid? GetStoredItem(EntityUid uid, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return null;

        return component.ItemContainer.ContainedEntity;
    }

    /// <summary>
    /// Checks if the storage is empty.
    /// </summary>
    public bool IsEmpty(EntityUid uid, ManualItemStorageComponent? component = null)
    {
        return GetStoredItem(uid, component) == null;
    }

    /// <summary>
    /// Networks the current state of the storage.
    /// Call this server-side after inserting or removing items.
    /// </summary>
    public void UpdateState(EntityUid uid, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        Dirty(uid, component);
    }

    /// <summary>
    /// Attempts to insert an item into the storage and networks the change.
    /// </summary>
    public bool TryInsertItemAndDirty(EntityUid uid, EntityUid item, ManualItemStorageComponent? component = null)
    {
        if (!TryInsertItem(uid, item, component))
            return false;

        UpdateState(uid, component);
        return true;
    }

    /// <summary>
    /// Attempts to remove the stored item and networks the change.
    /// </summary>
    public bool TryRemoveItemAndDirty(EntityUid uid, ManualItemStorageComponent? component = null)
    {
        if (!TryRemoveItem(uid, component))
            return false;

        UpdateState(uid, component);
        return true;
    }

    /// <summary>
    /// Attempts to insert the item in the user's active hand into the storage.
    /// Does NOT automatically dirty - use TryInsertItemInHandAndDirty for that.
    /// </summary>
    public bool TryInsertItemInHand(EntityUid uid, EntityUid user, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!_hands.TryGetActiveItem(user, out var item))
            return false;

        if (!TryInsertItem(uid, item.Value, component))
        {
            if (component.InsertFailPopup != null)
                _popup.PopupEntity(Loc.GetString(component.InsertFailPopup), uid, user);
            return false;
        }

        if (component.InsertSuccessPopup != null)
            _popup.PopupEntity(Loc.GetString(component.InsertSuccessPopup), uid, user);

        return true;
    }

    /// <summary>
    /// Attempts to insert the item in the user's active hand into the storage and networks the change.
    /// </summary>
    public bool TryInsertItemInHandAndDirty(EntityUid uid, EntityUid user, ManualItemStorageComponent? component = null)
    {
        if (!TryInsertItemInHand(uid, user, component))
            return false;

        UpdateState(uid, component);
        return true;
    }

    /// <summary>
    /// Attempts to remove the stored item and place it in the user's active hand.
    /// Does NOT automatically dirty - use TryRemoveItemToHandAndDirty for that.
    /// </summary>
    public bool TryRemoveItemToHand(EntityUid uid, EntityUid user, ManualItemStorageComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        var item = GetStoredItem(uid, component);
        if (item == null)
            return false;

        if (!TryRemoveItem(uid, component))
            return false;

        _hands.TryPickup(user, item.Value);
        return true;
    }

    /// <summary>
    /// Attempts to remove the stored item and place it in the user's active hand, then networks the change.
    /// </summary>
    public bool TryRemoveItemToHandAndDirty(EntityUid uid, EntityUid user, ManualItemStorageComponent? component = null)
    {
        if (!TryRemoveItemToHand(uid, user, component))
            return false;

        UpdateState(uid, component);
        return true;
    }
}
