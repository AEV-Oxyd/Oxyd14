using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydAttachmentHolderComponent : Component
{
    [DataField]
    public EntityWhitelist whitelist = new();
    [ViewVariables]
    public CompoundedModifiers mods = new();
    [DataField]
    public Dictionary<AttSlot, NetEntity> attachments = new();

    [DataField]
    public List<AttSlot> slots = new();
}

[Serializable, NetSerializable]
public sealed class OxydAttachmentHolderComponentState : ComponentState
{
    public readonly Dictionary<AttSlot, NetEntity> Attachments;
    public readonly List<AttSlot> Slots;
    public readonly CompoundedModifiers Mods;

    public OxydAttachmentHolderComponentState(Dictionary<AttSlot, NetEntity> attachments, List<AttSlot> slots, CompoundedModifiers mods)
    {
        Attachments = attachments;
        Slots = slots;
        Mods = mods;
    }
}

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydAttachmentSpawnerComponent : Component
{
    [DataField]
    public List<EntProtoId> insert = new();
}
