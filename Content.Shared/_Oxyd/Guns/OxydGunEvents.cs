using Robust.Shared.Map;
using Robust.Shared.Serialization;


namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public class ClientSideInterpretingFiremode : EntityEventArgs
{
    public NetEntity gun;
    public NetEntity shooter;
    public required OxydBaseGunFiremode firemode;
    public int clientsideStartingStep = 0;
    public MapCoordinates shotFrom;
    public MapCoordinates aimedPosition;
}

public class ClientSideDoneInterpretingFiremode : EntityEventArgs
{
    public required OxydBaseGunFiremode firemode;
    public int stoppedAt = 0;
}


[Serializable, NetSerializable]
public class ClientSideGunFiredEvent : EntityEventArgs
{
    public NetEntity gun;
    public NetEntity shooter;
    public MapCoordinates shotFrom;
    public MapCoordinates aimedPosition;
}
