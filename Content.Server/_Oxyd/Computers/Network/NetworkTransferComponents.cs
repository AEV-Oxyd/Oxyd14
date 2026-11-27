using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;

namespace Content.Server._Oxyd.Computers.Network;

/// <summary>
/// Active Component attached to a <see cref="NetworkRouterComponent"/> ONLY while one or more
/// time-based network file/data transfers are queued on that router.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveRouterTransferQueueComponent : Component
{
    public List<QueuedRouterTransfer> Transfers = new();
}

/// <summary>
/// Represents a single in-flight upload/download routed through a station router's shared bandwidth queue.
/// </summary>
public sealed class QueuedRouterTransfer
{
    public string TransferId = string.Empty;
    public string ConnectionId = string.Empty;
    public string Protocol = "TCP/XFER";
    public string TargetPath = string.Empty;
    public string Description = string.Empty;
    public bool IsUpload;

    public EntityUid SourceCompUid;
    public EntityUid TargetCompUid;
    public EntityUid? InitiatorProgramUid;

    public int TotalBytes;
    public int TransferredBytes;
    public float FractionalBytes;
    public TimeSpan NextProgressUpdate = TimeSpan.Zero;
    public bool AllowDebugFallback;

    public VirtualFile? PayloadFile;
    public NetworkDataPacket? PacketMetadata;

    public Action<QueuedRouterTransfer>? OnProgress;
    public Action<QueuedRouterTransfer>? OnCompleted;
    public Action<QueuedRouterTransfer, string>? OnAborted;
}
