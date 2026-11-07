using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Network;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class NetworkDataPacket
{
    [DataField]
    public string PacketId = string.Empty;

    [DataField]
    public string SourceAddress = string.Empty;

    [DataField]
    public string TargetAddress = string.Empty;

    [DataField]
    public string Protocol = "DATA";

    [DataField]
    public string PayloadSummary = string.Empty;

    [DataField]
    public int ByteSize = 0;

    [DataField]
    public TimeSpan Timestamp;

    [DataField]
    public bool Dropped = false;
}
