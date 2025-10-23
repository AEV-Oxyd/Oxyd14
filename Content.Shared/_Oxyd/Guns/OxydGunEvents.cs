using System.IO;
using Lidgren.Network;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;

public class ClientSideInterpretingFiremode : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetEntity gun;
    public NetEntity shooter;
    public int clientsideStartingStep = 0;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
       gun = buffer.ReadNetEntity();
       shooter = buffer.ReadNetEntity();
       clientsideStartingStep = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(gun);
        buffer.Write(shooter);
        buffer.Write(clientsideStartingStep);
    }
}

public class FiremodeClientsideFiredEvent : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetCoordinates aimedPosition;
    public NetCoordinates shotFrom;
    public int firemodeStep = 0;
    public NetEntity gun;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        aimedPosition = buffer.ReadNetCoordinates();
        shotFrom = buffer.ReadNetCoordinates();
        firemodeStep = buffer.ReadInt32();
        gun = buffer.ReadNetEntity();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(aimedPosition);
        buffer.Write(shotFrom);
        buffer.Write(firemodeStep);
        buffer.Write(gun);
    }
}
public class ClientSideDoneInterpretingFiremode : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetEntity gun;
    public int stoppedAt = 0;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        gun = buffer.ReadNetEntity();
        stoppedAt = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(gun);
        buffer.Write(stoppedAt);
    }
}


public class FiremodeProjectilesFiredEvent : EntityEventArgs
{
    public required List<Entity<OxydProjectileComponent>> projectiles;
    public EntityUid shooter = EntityUid.Invalid;
}
