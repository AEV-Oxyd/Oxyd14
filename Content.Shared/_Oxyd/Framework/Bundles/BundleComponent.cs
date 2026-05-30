using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class BundleComponent : Component
{
    public List<NetEntity> containing = new();

    public ProtoId<BundleGroup> group;

    public int usedVolume = 0;

}
