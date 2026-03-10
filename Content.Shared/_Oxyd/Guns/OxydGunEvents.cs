using System.IO;
using Lidgren.Network;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;
public sealed class ClientSideInterpretingFiremode : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetEntity gun;
    public int clientsideStartingStep = 0;
    public GameTick clientTick;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
       gun = buffer.ReadNetEntity();
       clientsideStartingStep = buffer.ReadInt32();
       clientTick = buffer.ReadGameTick();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(gun);
        buffer.Write(clientsideStartingStep);
        buffer.Write(clientTick);
    }
}
[NetSerializable, Serializable]
public sealed class SetGunChargeEvent : EntityEventArgs
{
    public float charge = 0f;
}
public sealed class FiremodeMouseStatus : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public bool held = false;
    public NetEntity gun;
    public GameTick clientTick;
    public int fromStep;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        held = buffer.ReadBoolean();
        gun = buffer.ReadNetEntity();
        clientTick = buffer.ReadGameTick();
        fromStep = buffer.ReadInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(held);
        buffer.Write(gun);
        buffer.Write(clientTick);
        buffer.Write(fromStep);
    }

}

public sealed class FiremodeClientsideFiredEvent : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetCoordinates aimedPosition;
    public NetCoordinates shotFrom;
    public int firemodeStep = 0;
    public NetEntity gun;
    public GameTick clientTick;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        aimedPosition = buffer.ReadNetCoordinates();
        shotFrom = buffer.ReadNetCoordinates();
        firemodeStep = buffer.ReadInt32();
        gun = buffer.ReadNetEntity();
        clientTick = buffer.ReadGameTick();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(aimedPosition);
        buffer.Write(shotFrom);
        buffer.Write(firemodeStep);
        buffer.Write(gun);
        buffer.Write(clientTick);
    }
}
public sealed class ClientSideDoneInterpretingFiremode : NetMessage
{
    public override MsgGroups MsgGroup { get; } = MsgGroups.EntityEvent;
    public NetEntity gun;
    public int stoppedAt = 0;
    public GameTick clientTick;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        gun = buffer.ReadNetEntity();
        stoppedAt = buffer.ReadInt32();
        clientTick = buffer.ReadGameTick();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(gun);
        buffer.Write(stoppedAt);
        buffer.Write(clientTick);
    }
}

public sealed class FiremodeProjectilesFiredEvent : EntityEventArgs
{
    public Entity<OxydGunComponent> gun;
    public required HashSet<Entity<OxydProjectileComponent>> projectiles;
    public EntityUid shooter = EntityUid.Invalid;
}
[Serializable, NetSerializable]
public sealed class FiremodeChangedEvent : EntityEventArgs
{
    public required NetEntity gun;
    public required NetEntity switcher;
    public required int index;
}

[Serializable, NetSerializable]
public sealed class GunSafetyChangedEvent : EntityEventArgs
{
    public required NetEntity gun;
    public required NetEntity switcher;
    public required bool newState;
}

public sealed  class GunFiredEvent : EntityEventArgs
{
    public required HashSet<Entity<OxydProjectileComponent>> projectiles;

    public GameTick simTick;
}

public sealed class GunBeforeFireIndividualProjectileEvent : EntityEventArgs
{
    public required Entity<OxydProjectileComponent> projectile;
    public GameTick simTick;
}

public sealed class GunAfterFireIndividualProjectileEvent : EntityEventArgs
{
    public required Entity<OxydProjectileComponent> projectile;
    public GameTick simTick;
}


public sealed class GunGetInaccuracyEvent : EntityEventArgs
{
    public required Angle baseInaccuracy;
    public required Angle addedInaccuracy;
    public required GameTick simTick;
}
[Serializable, NetSerializable]
public sealed class GunCompareFired : EntityEventArgs
{
    public int firedCount;
    public NetEntity target;
}


