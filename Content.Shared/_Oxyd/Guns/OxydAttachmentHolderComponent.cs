using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydAttachmentHolderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityWhitelist whitelist = new();
    [ViewVariables, AutoNetworkedField]
    public CompoundedModifiers mods = new();
    [DataField, AutoNetworkedField]
    public Dictionary<AttSlot, NetEntity> attachments = new();

    [DataField, AutoNetworkedField]
    public List<AttSlot> slots = new();
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
