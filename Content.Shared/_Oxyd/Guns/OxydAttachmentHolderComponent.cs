using Content.Shared.Whitelist;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydAttachmentHolderComponent : Component
{
    [DataField]
    public EntityWhitelist allowedAttachments = new();
    [DataField]
    public Dictionary<AttSlots, EntityUid> attachments = new();
}
