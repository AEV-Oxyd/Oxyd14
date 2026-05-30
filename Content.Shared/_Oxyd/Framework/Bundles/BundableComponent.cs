using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class BundableComponent : Component
{
    public ProtoId<BundleGroup> group;
    public int volume = 1;
}
