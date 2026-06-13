using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class BundleComponent : Component
{
    public static readonly Vector2 unsetVector = new Vector2(float.NaN, float.NaN);
    [AutoNetworkedField, ViewVariables]
    public List<NetEntity> containing = new();
    [ViewVariables, AutoNetworkedField]
    public ProtoId<BundleGroup> group = "BundleGroup";
    [AutoNetworkedField, ViewVariables]
    public int usedVolume = 0;
    [ViewVariables, AutoNetworkedField]
    public int checksum = 0;
    [ViewVariables, AutoNetworkedField]
    public Dictionary<NetEntity, Vector2> bundlePositions = new();

}
