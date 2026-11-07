using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Network;

[Serializable, NetSerializable]
public sealed class ActiveNetworkConnectionRecord
{
    public string ConnectionId = string.Empty;
    public string SourceGuid = string.Empty;
    public string TargetGuid = string.Empty;
    public string ConnectionType = string.Empty;
    public string Description = string.Empty;
}

/// <summary>
/// Attached to router computers (such as StationNetworkRouter).
/// Records routed packets, tracks active connections (Downloads, Uploads, SSH, Screen Shares),
/// maintains DNS service mappings, and enforces GUID blacklists.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetworkRouterComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<string> BlacklistedGuids = new();

    [DataField, AutoNetworkedField]
    public HashSet<string> ConnectedDevices = new();

    [DataField, AutoNetworkedField]
    public Dictionary<string, string> DnsRecords = new();

    [DataField, AutoNetworkedField]
    public List<string> ConnectionLogs = new();

    [ViewVariables]
    public List<NetworkDataPacket> RecordedPackets = new();

    [ViewVariables]
    public List<ActiveNetworkConnectionRecord> ActiveConnections = new();

    [DataField]
    public int MaxRecordedPackets = 150;

    [DataField]
    public int MaxConnectionLogs = 150;
}
