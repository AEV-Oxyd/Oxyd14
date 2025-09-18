using Robust.Shared.Map;
using Robust.Shared.Serialization;


namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public class ClientSideGunFiredEvent : EntityEventArgs
{
    public NetEntity gun;
    public NetEntity shooter;
    public MapCoordinates shotFrom;
    // where was actually clicked
    public MapCoordinates clickPosition;
    // where it was actually aimed , might be relative since it might be a moving grid
    public MapCoordinates aimedPosition;
}
