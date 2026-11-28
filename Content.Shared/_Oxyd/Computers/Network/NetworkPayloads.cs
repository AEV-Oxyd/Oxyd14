using Content.Shared.DeviceNetwork;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Network;

[Serializable, NetSerializable]
public sealed partial class FileTransferPayload : INetworkPayload
{
    public string Path { get; set; } = "/";
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string FileContent { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class DdosFloodPayload : INetworkPayload
{
    public string FloodData { get; set; } = new string('X', 1024); // 1 KB string flood payload
}

[Serializable, NetSerializable]
public sealed partial class ServiceRequestPayload : INetworkPayload
{
    public string ServiceName { get; set; } = string.Empty; // e.g. "ManifestService"
    public string QueryParameter { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class PingPayload : INetworkPayload
{
    public string SenderGuid { get; set; } = string.Empty;
    public TimeSpan SentTime { get; set; }
}

[Serializable, NetSerializable]
public sealed partial class PongPayload : INetworkPayload
{
    public string TargetGuid { get; set; } = string.Empty;
    public TimeSpan SentTime { get; set; }
    public bool IsSaturated { get; set; }
    public int QueuedPackets { get; set; }
    public int BandwidthLimitBps { get; set; }
}

[Serializable, NetSerializable]
public sealed partial class FileDownloadRequestPayload : INetworkPayload
{
    public string SenderGuid { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class FileDeleteRequestPayload : INetworkPayload
{
    public string SenderGuid { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class FileDeleteResponsePayload : INetworkPayload
{
    public string TargetPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class RemoteControlRequestPayload : INetworkPayload
{
    public string SenderGuid { get; set; } = string.Empty;
    public string TargetGuid { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty; // e.g. "status", "reboot", "power_toggle"
    public string AuthToken { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed partial class RemoteControlResponsePayload : INetworkPayload
{
    public string Command { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ResultMessage { get; set; } = string.Empty;
}
