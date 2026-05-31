using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BundleComponent : Component
{
    [AutoNetworkedField, ViewVariables]
    public List<NetEntity> containing = new();

    [ViewVariables]
    public ProtoId<BundleGroup> group;

    [AutoNetworkedField, ViewVariables]
    public int usedVolume = 0;

    [ViewVariables, AutoNetworkedField]
    public int checksum = 0;

}
