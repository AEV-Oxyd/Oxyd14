using Content.Shared._Oxyd.Computers.Network;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent]
public sealed partial class RouterProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Router Ready";
}

[Serializable, NetSerializable]
public sealed class RouterConnectedDeviceDto
{
    public string Name { get; }
    public string Guid { get; }
    public int DownloadBps { get; }
    public int UploadBps { get; }
    public bool IsOnline { get; }
    public bool IsBlacklisted { get; }

    public RouterConnectedDeviceDto(
        string name,
        string guid,
        int downloadBps,
        int uploadBps,
        bool isOnline,
        bool isBlacklisted)
    {
        Name = name;
        Guid = guid;
        DownloadBps = downloadBps;
        UploadBps = uploadBps;
        IsOnline = isOnline;
        IsBlacklisted = isBlacklisted;
    }
}

[Serializable, NetSerializable]
public sealed class RouterUiState : BoundUserInterfaceState
{
    public string RouterGuid { get; }
    public bool IsPowered { get; }
    public int BandwidthBps { get; }
    public int UploadBps { get; }
    public List<RouterConnectedDeviceDto> ConnectedDevices { get; }
    public List<ActiveNetworkConnectionRecord> Connections { get; }
    public List<NetworkDataPacket> Packets { get; }
    public List<string> BlacklistedGuids { get; }
    public Dictionary<string, string> DnsRecords { get; }
    public string StatusMessage { get; }

    public RouterUiState(
        string routerGuid,
        bool isPowered,
        int bandwidthBps,
        int uploadBps,
        List<RouterConnectedDeviceDto> connectedDevices,
        List<ActiveNetworkConnectionRecord> connections,
        List<NetworkDataPacket> packets,
        List<string> blacklistedGuids,
        Dictionary<string, string> dnsRecords,
        string statusMessage)
    {
        RouterGuid = routerGuid;
        IsPowered = isPowered;
        BandwidthBps = bandwidthBps;
        UploadBps = uploadBps;
        ConnectedDevices = connectedDevices;
        Connections = connections;
        Packets = packets;
        BlacklistedGuids = blacklistedGuids;
        DnsRecords = dnsRecords;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum RouterUiAction : byte
{
    Refresh,
    KillConnection,
    BlacklistGuid,
    UnblacklistGuid,
    SetDns,
    RemoveDns,
    ClearPackets
}

[Serializable, NetSerializable]
public sealed class RouterActionMessage : BoundUserInterfaceMessage
{
    public RouterUiAction Action { get; }
    public string TargetId { get; }
    public string Value { get; }

    public RouterActionMessage(RouterUiAction action, string targetId = "", string value = "")
    {
        Action = action;
        TargetId = targetId;
        Value = value;
    }
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PacketSnifferProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Monitoring network bus";
}

[Serializable, NetSerializable]
public sealed class PacketSnifferUiState : BoundUserInterfaceState
{
    public string RouterGuid { get; }
    public bool HasNetworkCard { get; }
    public List<NetworkDataPacket> Packets { get; }
    public string StatusMessage { get; }

    public PacketSnifferUiState(
        string routerGuid,
        bool hasNetworkCard,
        List<NetworkDataPacket> packets,
        string statusMessage)
    {
        RouterGuid = routerGuid;
        HasNetworkCard = hasNetworkCard;
        Packets = packets;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum PacketSnifferAction : byte
{
    Refresh,
    ClearLogs,
    SaveLogsToFile,
    EmitPacket,
    ModifyAndTransmitPacket
}

[Serializable, NetSerializable]
public sealed class PacketSnifferActionMessage : BoundUserInterfaceMessage
{
    public PacketSnifferAction Action { get; }
    public string SourceGuid { get; }
    public string TargetGuid { get; }
    public string Protocol { get; }
    public string Payload { get; }
    public string PacketId { get; }
    public Dictionary<string, string>? ModifiedEntries { get; }
    public bool ApplyToSource { get; }

    public PacketSnifferActionMessage(
        PacketSnifferAction action,
        string sourceGuid = "",
        string targetGuid = "",
        string protocol = "TCP/RAW",
        string payload = "",
        string packetId = "",
        Dictionary<string, string>? modifiedEntries = null,
        bool applyToSource = false)
    {
        Action = action;
        SourceGuid = sourceGuid;
        TargetGuid = targetGuid;
        Protocol = protocol;
        Payload = payload;
        PacketId = packetId;
        ModifiedEntries = modifiedEntries;
        ApplyToSource = applyToSource;
    }
}
