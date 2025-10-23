using Robust.Shared.Map;
using Robust.Shared.Serialization;


namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public class ClientSideInterpretingFiremode : EntityEventArgs
{
    public NetEntity gun;
    public NetEntity shooter;
    public int clientsideStartingStep = 0;
}

[Serializable, NetSerializable]
public class FiremodeClientsideFiredEvent : EntityEventArgs
{
    public MapCoordinates aimedPosition;
    public MapCoordinates shotFrom;
    public int firemodeStep = 0;
    public NetEntity gun;
}

[Serializable, NetSerializable]
public class ClientSideDoneInterpretingFiremode : EntityEventArgs
{
    public NetEntity gun;
    public int stoppedAt = 0;
}


public class FiremodeProjectilesFiredEvent : EntityEventArgs
{
    public required List<Entity<OxydProjectileComponent>> projectiles;
    public EntityUid shooter = EntityUid.Invalid;
}
