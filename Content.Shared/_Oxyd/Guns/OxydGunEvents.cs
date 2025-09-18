using Robust.Shared.Map;
using Robust.Shared.Serialization;


namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public class ClientSideGunFiredEvent : EntityEventArgs
{
    public NetEntity gun;
    public NetEntity shooter;
    public MapCoordinates shotFrom;
    public MapCoordinates aimedPosition;
}
