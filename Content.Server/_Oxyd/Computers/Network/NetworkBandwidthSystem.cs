using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

public sealed partial class NetworkBandwidthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<NetworkBandwidthComponent> _bwQuery;
    private EntityQuery<NetworkRouterComponent> _routerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _bwQuery = GetEntityQuery<NetworkBandwidthComponent>();
        _routerQuery = GetEntityQuery<NetworkRouterComponent>();

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
            comp.CurrentBytesSent = 0;

            var drainRate = Math.Max(10, comp.MaxBps / 1024);
            comp.CurrentQueuedPackets = Math.Max(0, comp.CurrentQueuedPackets - drainRate);
            var maxQueue = comp.BufferQueueLimit * 2;
            comp.CurrentQueuedPackets = Math.Min(comp.CurrentQueuedPackets, maxQueue);

            var wasSaturated = comp.IsSaturated;
            comp.IsSaturated = comp.CurrentQueuedPackets > comp.BufferQueueLimit;
            if (wasSaturated != comp.IsSaturated)
                Dirty(uid, comp);
        }
    }

    private void OnBeforePacketSent(EntityUid uid, NetworkBandwidthComponent comp, ref BeforePacketSentEvent args)
    {
        if (comp.MaxUploadBps <= 0 || comp.IsSaturated)
            args.Cancelled = true;
    }

    private void OnPacketReceived(EntityUid uid, NetworkBandwidthComponent comp, ref DeviceNetworkPacketEvent<INetworkPayload> args)
    {
        if (comp.MaxBps <= 0)
            return;

        var summary = args.Data.GetType().Name;
        var byteSize = Math.Max(64, summary.Length * 4 + 32);

        comp.CurrentQueuedPackets++;
        if (comp.CurrentQueuedPackets > comp.BufferQueueLimit)
            comp.IsSaturated = true;

        var dropped = comp.IsSaturated;
        if (!dropped)
            comp.CurrentBytesReceived += byteSize;

        var localAddr = "LOCAL";
        if (TryComp<DeviceNetworkComponent>(uid, out var netComp))
        {
            var addrVal = netComp.Address;
            localAddr = addrVal.ToString();
        }
        var pkt = StorePacket(
            uid,
            args.SenderAddress.ToString(),
            localAddr,
            "DEVNET",
            summary,
            byteSize,
            isEncrypted: false,
            rawPayload: $"sender={args.SenderAddress};type={summary}",
            dropped: dropped,
            comp: comp);

        LogPacketToRouters(pkt, senderUid: args.Sender);
    }

    public NetworkDataPacket StorePacket(
        EntityUid uid,
        string sourceAddr,
        string targetAddr,
        string protocol,
        string summary,
        int byteSize,
        bool isEncrypted = false,
        string rawPayload = "",
        bool dropped = false,
        NetworkBandwidthComponent? comp = null)
    {
        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = sourceAddr,
            TargetAddress = targetAddr,
            Protocol = protocol,
            PayloadSummary = summary,
            RawPayload = rawPayload,
            IsEncrypted = isEncrypted,
            ByteSize = byteSize,
            Timestamp = _timing.CurTime,
            Dropped = dropped
        };

        if (!_bwQuery.Resolve(uid, ref comp, false))
            return pkt;

        comp.StoredPackets.Add(pkt);
        while (comp.StoredPackets.Count > comp.MaxStoredPackets)
        {
            comp.StoredPackets.RemoveAt(0);
        }

        Dirty(uid, comp);
        return pkt;
    }

    public void LogPacketToRouters(NetworkDataPacket pkt, EntityUid? senderUid = null)
    {
        var routerQuery = EntityQueryEnumerator<NetworkRouterComponent>();
        while (routerQuery.MoveNext(out var rUid, out var netRouter))
        {
            if (TryComp<EmbeddedComputerHostComponent>(rUid, out var host) && host.ComputerEntity is { } hostComp)
            {
                if (!TryComp<OxydComputerComponent>(hostComp, out var oxyd) || !oxyd.IsPowered)
                    continue;
            }

            if (_bwQuery.TryComp(rUid, out var rBw))
            {
                rBw.StoredPackets.Add(pkt);
                while (rBw.StoredPackets.Count > rBw.MaxStoredPackets)
                {
                    rBw.StoredPackets.RemoveAt(0);
                }
                Dirty(rUid, rBw);
            }

            netRouter.RecordedPackets.Add(pkt);
            while (netRouter.RecordedPackets.Count > netRouter.MaxRecordedPackets)
            {
                netRouter.RecordedPackets.RemoveAt(0);
            }

            Dirty(rUid, netRouter);
        }
    }

    public void RecordDataTransmitted(EntityUid uid, int byteCount, bool isUpload = false, NetworkBandwidthComponent? comp = null)
    {
        if (!_bwQuery.Resolve(uid, ref comp, false))
            return;

        if (isUpload)
            comp.CurrentBytesSent += Math.Max(0, byteCount);
        else
            comp.CurrentBytesReceived += Math.Max(0, byteCount);

        var limit = isUpload ? comp.MaxUploadBps : comp.MaxBps;
        if (limit > 0 && (isUpload ? comp.CurrentBytesSent : comp.CurrentBytesReceived) > limit * 2)
        {
            comp.CurrentQueuedPackets++;
            if (comp.CurrentQueuedPackets > comp.BufferQueueLimit)
                comp.IsSaturated = true;
        }

        Dirty(uid, comp);
    }

    public int GetEffectiveTransferRateBps(EntityUid senderUid, EntityUid receiverUid)
    {
        var up = _bwQuery.TryComp(senderUid, out var sBw) && sBw.MaxUploadBps > 0 ? sBw.MaxUploadBps : 0;
        var down = _bwQuery.TryComp(receiverUid, out var rBw) && rBw.MaxBps > 0 ? rBw.MaxBps : 0;

        if (up <= 0 || down <= 0)
            return 0;

        var rate = Math.Min(up, down);
        var routerQuery = EntityQueryEnumerator<NetworkRouterComponent>();
        if (routerQuery.MoveNext(out var rUid, out _) && _bwQuery.TryComp(rUid, out var routerBw) && routerBw.MaxBps > 0)
            rate = Math.Min(rate, routerBw.MaxBps);

        return rate;
    }

    public void RecordScreenSharingBandwidth(EntityUid targetUid, EntityUid? viewerTerminalUid = null, int byteCount = 2048)
    {
        if (!_bwQuery.TryComp(targetUid, out var targetBw) || targetBw.IsSaturated)
            return;

        RecordDataTransmitted(targetUid, byteCount, isUpload: true, targetBw);

        if (viewerTerminalUid is { } term && _bwQuery.TryComp(term, out var viewerBw))
            RecordDataTransmitted(term, byteCount, isUpload: false, viewerBw);
    }
}
