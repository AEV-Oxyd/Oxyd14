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
    public string RawPayload = string.Empty;

    [DataField]
    public bool IsEncrypted = false;

    [DataField]
    public bool IsPlayerRelevant = false;

    [DataField]
    public bool IsEditable = false;

    [DataField]
    public string FilePath = string.Empty;

    [DataField]
    public Dictionary<string, string> EditableEntries = new();

    [DataField]
    public Dictionary<string, string> Parameters = new();

    [DataField]
    public int ByteSize = 0;

    [DataField]
    public TimeSpan Timestamp;

    [DataField]
    public bool Dropped = false;

    public NetworkDataPacket Clone()
    {
        return new NetworkDataPacket
        {
            PacketId = PacketId,
            SourceAddress = SourceAddress,
            TargetAddress = TargetAddress,
            Protocol = Protocol,
            PayloadSummary = PayloadSummary,
            RawPayload = RawPayload,
            IsEncrypted = IsEncrypted,
            IsPlayerRelevant = IsPlayerRelevant,
            IsEditable = IsEditable,
            FilePath = FilePath,
            EditableEntries = new Dictionary<string, string>(EditableEntries),
            Parameters = new Dictionary<string, string>(Parameters),
            ByteSize = ByteSize,
            Timestamp = Timestamp,
            Dropped = Dropped
        };
    }
}
