using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Framework.Bundles;
/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BundleComponent : Component
{
    [Serializable, NetSerializable]
    public struct BundleEntData
    {
        public Vector2 pos;
        public Angle storeAngle;
    }
    [ViewVariables]
    public ProtoId<BundleGroup> group = "BundleGroup";
    [ViewVariables, AutoNetworkedField]
    public int usedVolume = 0;
    [ViewVariables]
    public Dictionary<EntityUid, BundleEntData> bundlePositions = new();
}
