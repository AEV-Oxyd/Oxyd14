using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

public sealed partial class NetworkBandwidthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetworkBandwidthComponent, BeforePacketSentEvent>(OnBeforePacketSent);
        SubscribeLocalEvent<NetworkBandwidthComponent, DeviceNetworkPacketEvent<INetworkPayload>>(OnPacketReceived);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<NetworkBandwidthComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (curTime < comp.NextRateReset)
                continue;

            comp.NextRateReset = curTime + TimeSpan.FromSeconds(1);
            comp.CurrentBytesReceived = 0;

            // Drain queued packets based on maximum processing capacity
            var drainRate = Math.Max(10, comp.MaxBps / 1024);
            comp.CurrentQueuedPackets = Math.Max(0, comp.CurrentQueuedPackets - drainRate);

            var wasSaturated = comp.IsSaturated;
            // Node remains saturated as long as backlog exceeds buffer queue limit
            comp.IsSaturated = comp.CurrentQueuedPackets > comp.BufferQueueLimit;
            if (wasSaturated != comp.IsSaturated)
                Dirty(uid, comp);
        }
    }

    private void OnBeforePacketSent(EntityUid uid, NetworkBandwidthComponent comp, ref BeforePacketSentEvent args)
    {
        // When link bandwidth is exhausted or queued packets exceed buffer limit, packets are dropped
        if (comp.IsSaturated)
        {
            args.Cancelled = true;
        }
    }

    private void OnPacketReceived(EntityUid uid, NetworkBandwidthComponent comp, ref DeviceNetworkPacketEvent<INetworkPayload> args)
    {
        var approxSize = 128; // Standard packet header / routing metadata overhead

        switch (args.Data)
        {
            case FileTransferPayload ft:
                approxSize += ft.FileContent.Length + ft.FileName.Length + ft.Path.Length;
                break;
            case DdosFloodPayload df:
                approxSize += df.FloodData.Length;
                break;
            case FileDownloadRequestPayload dr:
                approxSize += dr.TargetPath.Length + dr.AuthToken.Length;
                break;
            case FileDeleteRequestPayload del:
                approxSize += del.TargetPath.Length + del.AuthToken.Length;
                break;
            case RemoteControlRequestPayload rc:
                approxSize += rc.Command.Length + rc.AuthToken.Length;
                break;
            default:
                approxSize += 128;
                break;
        }

        comp.CurrentBytesReceived += approxSize;

        // Bandwidth exhaustion occurs whenever traffic (normal file operations or DDoS floods) exceeds MaxBps
        if (comp.CurrentBytesReceived > comp.MaxBps)
        {
            var overflowPackets = 1 + (approxSize / 512);
            comp.CurrentQueuedPackets += overflowPackets;

            if (comp.CurrentQueuedPackets > comp.BufferQueueLimit)
            {
                if (!comp.IsSaturated)
                {
                    comp.IsSaturated = true;
                    Dirty(uid, comp);
                }
            }
        }
    }

    /// <summary>
    /// Explicitly records outgoing data transmission against the entity's network card.
    /// </summary>
    public void RecordDataTransmitted(EntityUid uid, int byteCount, NetworkBandwidthComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        comp.CurrentBytesReceived += byteCount;
        if (comp.CurrentBytesReceived > comp.MaxBps)
        {
            comp.CurrentQueuedPackets += 1 + (byteCount / 512);
            if (comp.CurrentQueuedPackets > comp.BufferQueueLimit && !comp.IsSaturated)
            {
                comp.IsSaturated = true;
                Dirty(uid, comp);
            }
        }
    }
}
