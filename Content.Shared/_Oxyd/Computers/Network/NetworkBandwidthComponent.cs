using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Network;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetworkBandwidthComponent : Component
{
    /// <summary>
    /// Maximum bytes per second this entity (or router) can forward/process.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxBps = 250000; // 250 KB/s (~244 KiB/s) default

    /// <summary>
    /// Current throughput utilized in the current evaluation window.
    /// </summary>
    [ViewVariables]
    public int CurrentBytesReceived = 0;

    /// <summary>
    /// Maximum number of buffered queued packets before packet drop begins.
    /// </summary>
    [DataField]
    public int BufferQueueLimit = 100;

    /// <summary>
    /// Current packets queued waiting for bandwidth allotment.
    /// </summary>
    [ViewVariables]
    public int CurrentQueuedPackets = 0;

    /// <summary>
    /// Flag indicating whether the node is currently experiencing DDoS packet exhaustion.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public bool IsSaturated = false;

    /// <summary>
    /// Stored data packets retained by this router or network device.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<NetworkDataPacket> StoredPackets = new();

    /// <summary>
    /// Maximum number of data packets this device or router can buffer/store.
    /// </summary>
    [DataField]
    public int MaxStoredPackets = 64;

    /// <summary>
    /// Timestamp for resetting the 1-second rolling rate window.
    /// </summary>
    public TimeSpan NextRateReset = TimeSpan.Zero;
}
