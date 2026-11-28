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
    public bool IsLocked { get; }

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
        string statusMessage,
        bool isLocked = false)
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
        IsLocked = isLocked;
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

    [DataField]
    public string SelectedTargetGuid = string.Empty;

    [DataField]
    public string SelectedTargetName = string.Empty;
}

/// <summary>
/// Reachable DeviceLink sink port on the selected target device.
/// </summary>
[Serializable, NetSerializable]
public sealed class SnifferSinkTargetDto
{
    public string TargetGuid { get; }
    public string DeviceName { get; }
    public string SinkPortId { get; }
    public string SinkPortName { get; }
    public bool IsLocal { get; }
    public bool IsHostPort { get; }

    public SnifferSinkTargetDto(
        string targetGuid,
        string deviceName,
        string sinkPortId,
        string sinkPortName,
        bool isLocal,
        bool isHostPort)
    {
        TargetGuid = targetGuid;
        DeviceName = deviceName;
        SinkPortId = sinkPortId;
        SinkPortName = sinkPortName;
        IsLocal = isLocal;
        IsHostPort = isHostPort;
    }
}

[Serializable, NetSerializable]
public sealed class PacketSnifferUiState : BoundUserInterfaceState
{
    public string RouterGuid { get; }
    public string LocalGuid { get; }
    public bool HasNetworkCard { get; }
    public List<NetworkDataPacket> Packets { get; }
    public List<SnifferSinkTargetDto> SinkTargets { get; }
    public string StatusMessage { get; }
    public string SelectedTargetGuid { get; }
    public string SelectedTargetName { get; }

    public PacketSnifferUiState(
        string routerGuid,
        string localGuid,
        bool hasNetworkCard,
        List<NetworkDataPacket> packets,
        List<SnifferSinkTargetDto> sinkTargets,
        string statusMessage,
        string selectedTargetGuid = "",
        string selectedTargetName = "")
    {
        RouterGuid = routerGuid;
        LocalGuid = localGuid;
        HasNetworkCard = hasNetworkCard;
        Packets = packets;
        SinkTargets = sinkTargets;
        StatusMessage = statusMessage;
        SelectedTargetGuid = selectedTargetGuid;
        SelectedTargetName = selectedTargetName;
    }
}

[Serializable, NetSerializable]
public enum PacketSnifferAction : byte
{
    Refresh,
    ClearLogs,
    SaveLogsToFile,
    EmitPacket,
    ModifyAndTransmitPacket,
    SelectTargetDevice
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
    public string SinkPortId { get; }
    public bool IsHostPort { get; }

    public PacketSnifferActionMessage(
        PacketSnifferAction action,
        string sourceGuid = "",
        string targetGuid = "",
        string protocol = "TCP/RAW",
        string payload = "",
        string packetId = "",
        Dictionary<string, string>? modifiedEntries = null,
        string sinkPortId = "",
        bool isHostPort = true)
    {
        Action = action;
        SourceGuid = sourceGuid;
        TargetGuid = targetGuid;
        Protocol = protocol;
        Payload = payload;
        PacketId = packetId;
        ModifiedEntries = modifiedEntries;
        SinkPortId = sinkPortId;
        IsHostPort = isHostPort;
    }
}

// =============================================================================
// NetNavigator Program (/bin/net_navigator.nte) & Network Discovery Probe DTOs
// =============================================================================

/// <summary>
/// Metadata advertised by a reachable network device responding to a network discovery probe.
/// </summary>
[Serializable, NetSerializable]
public sealed class DiscoverableNetworkDeviceDto
{
    public string Guid { get; }
    public string Name { get; }
    public string DeviceType { get; }
    public bool AuthRequired { get; }
    public string AuthSummary { get; }
    public string ExtraInfo { get; }
    public int SinkPortCount { get; }
    public bool IsLocal { get; }

    public DiscoverableNetworkDeviceDto(
        string guid,
        string name,
        string deviceType,
        bool authRequired,
        string authSummary,
        string extraInfo,
        int sinkPortCount,
        bool isLocal = false)
    {
        Guid = guid;
        Name = name;
        DeviceType = deviceType;
        AuthRequired = authRequired;
        AuthSummary = authSummary;
        ExtraInfo = extraInfo;
        SinkPortCount = sinkPortCount;
        IsLocal = isLocal;
    }
}

[RegisterComponent, NetworkedComponent]
public sealed partial class NetNavigatorProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Ready — click Probe Network to discover reachable devices.";

    [DataField]
    public TimeSpan? LastProbeTime;

    public List<DiscoverableNetworkDeviceDto> LastDevices = new();
}

[Serializable, NetSerializable]
public sealed class NetNavigatorUiState : BoundUserInterfaceState
{
    public string RouterGuid { get; }
    public string LocalGuid { get; }
    public bool HasNetworkCard { get; }
    public bool IsOnline { get; }
    public bool LocalDiscoverable { get; }
    public List<DiscoverableNetworkDeviceDto> Devices { get; }
    public string StatusMessage { get; }
    public TimeSpan? LastProbeTime { get; }

    public NetNavigatorUiState(
        string routerGuid,
        string localGuid,
        bool hasNetworkCard,
        bool isOnline,
        bool localDiscoverable,
        List<DiscoverableNetworkDeviceDto> devices,
        string statusMessage,
        TimeSpan? lastProbeTime = null)
    {
        RouterGuid = routerGuid;
        LocalGuid = localGuid;
        HasNetworkCard = hasNetworkCard;
        IsOnline = isOnline;
        LocalDiscoverable = localDiscoverable;
        Devices = devices;
        StatusMessage = statusMessage;
        LastProbeTime = lastProbeTime;
    }
}

[Serializable, NetSerializable]
public enum NetNavigatorAction : byte
{
    Probe,
    ToggleLocalDiscoverable
}

[Serializable, NetSerializable]
public sealed class NetNavigatorActionMessage : BoundUserInterfaceMessage
{
    public NetNavigatorAction Action { get; }

    public NetNavigatorActionMessage(NetNavigatorAction action)
    {
        Action = action;
    }
}
