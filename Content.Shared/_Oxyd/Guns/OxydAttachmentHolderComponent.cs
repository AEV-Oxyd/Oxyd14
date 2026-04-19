using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydAttachmentHolderComponent : Component
{
    [DataField]
    public EntityWhitelist allowedAttachments = new();

    [ViewVariables]
    public CompoundedModifiers mods = new();
    [ViewVariables]
    public Dictionary<AttSlots, EntityUid> attachments = new();
    [DataField]
    public List<EntProtoId> starting = default!;
}
