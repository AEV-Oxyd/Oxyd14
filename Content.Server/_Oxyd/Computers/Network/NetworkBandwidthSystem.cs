using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
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
        var protocol = "DATA";
        var summary = args.Data.GetType().Name;

        switch (args.Data)
        {
            case FileTransferPayload ft:
                approxSize += ft.FileContent.Length + ft.FileName.Length + ft.Path.Length;
                protocol = "TCP/FILE";
                summary = $"Transfer {ft.FileName}.{ft.Extension} ({approxSize} B)";
                break;
            case DdosFloodPayload df:
                approxSize += df.FloodData.Length;
                protocol = "UDP/FLOOD";
                summary = $"DDoS Flood Packet ({df.FloodData.Length} B)";
                break;
            case FileDownloadRequestPayload dr:
                approxSize += dr.TargetPath.Length + dr.AuthToken.Length;
                protocol = "HTTP/REQ";
                summary = $"Download request: {dr.TargetPath}";
                break;
            case FileDeleteRequestPayload del:
                approxSize += del.TargetPath.Length + del.AuthToken.Length;
                protocol = "HTTP/DEL";
                summary = $"Delete request: {del.TargetPath}";
                break;
            case PingPayload ping:
                protocol = "ICMP/PING";
                summary = $"Ping from {ping.SenderGuid}";
                break;
            case PongPayload pong:
                protocol = "ICMP/PONG";
                summary = $"Pong to {pong.TargetGuid}";
                break;
            case RemoteControlRequestPayload rc:
                approxSize += rc.Command.Length + rc.AuthToken.Length;
                protocol = "RPC/CMD";
                summary = $"Remote command: {rc.Command}";
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

        var senderEntity = args.Sender;
        var sourceStr = (senderEntity != null && TryComp<DeviceGuidComponent>(senderEntity, out var srcGuid))
            ? srcGuid.Guid
            : (args.SenderAddress != 0 ? $"NET:{args.SenderAddress}" : "SRC-UNKNOWN");

        var targetStr = TryComp<DeviceGuidComponent>(uid, out var tgtGuid)
            ? tgtGuid.Guid
            : $"DEV:{uid}";

        StorePacket(uid, sourceStr, targetStr, protocol, summary, approxSize, comp.IsSaturated, comp);
        LogPacketToRouters(sourceStr, targetStr, protocol, summary, approxSize, comp.IsSaturated);
    }

    /// <summary>
    /// Stores a data packet in the entity's network packet buffer.
    /// </summary>
    public void StorePacket(
        EntityUid uid,
        string source,
        string target,
        string protocol,
        string summary,
        int byteSize,
        bool dropped = false,
        NetworkBandwidthComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        var packet = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8],
            SourceAddress = source,
            TargetAddress = target,
            Protocol = protocol,
            PayloadSummary = summary,
            ByteSize = byteSize,
            Timestamp = _timing.CurTime,
            Dropped = dropped
        };

        comp.StoredPackets.Add(packet);
        if (comp.StoredPackets.Count > comp.MaxStoredPackets)
        {
            comp.StoredPackets.RemoveAt(0);
        }

        Dirty(uid, comp);
    }

    /// <summary>
    /// Broadcasts a data packet to all station routers and data servers for network monitoring, traffic routing, and auditing.
    /// </summary>
    public void LogPacketToRouters(string source, string target, string protocol, string summary, int byteSize, bool dropped = false)
    {
        var routerQuery = EntityQueryEnumerator<DeviceNetworkRouterComponent, NetworkBandwidthComponent>();
        while (routerQuery.MoveNext(out var rUid, out _, out var rBw))
        {
            StorePacket(rUid, source, target, protocol, summary, byteSize, dropped, rBw);
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
