using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class BundableComponent : Component
{
    [DataField("group")]
    public ProtoId<BundleGroup> group;
    [DataField("volume")]
    public int volume = 1;
}
