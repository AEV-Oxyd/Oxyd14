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
