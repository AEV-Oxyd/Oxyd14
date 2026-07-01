using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Framework.Bundles;
/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BundleComponent : Component
{
    [Serializable, NetSerializable]
    public struct BundleAct
    {
        public char id;
        public NetEntity entity;
    }

    public static readonly Vector2 unsetVector = new Vector2(float.NaN, float.NaN);
    [ViewVariables]
    public List<NetEntity> containing = new();
    [ViewVariables]
    public ProtoId<BundleGroup> group = "BundleGroup";
    [ViewVariables]
    public int usedVolume = 0;

    [ViewVariables]
    public List<BundleAct> checksum = new();
    [ViewVariables]
    public Dictionary<NetEntity, Vector2> bundlePositions = new();
    [Serializable, NetSerializable]
    public class BundleState : IComponentState
    {
        public List<NetEntity> Containing = new();
        public int UsedVolume;
        public List<BundleAct> Checksum = new();
        public Dictionary<NetEntity, Vector2> BundlePositions = new();
        public ProtoId<BundleGroup> Group;
    }

}
