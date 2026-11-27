using System.Linq;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

/// <summary>
/// Active-component network bandwidth manager and per-router time-based transfer queue.
/// Only iterates entities with <see cref="ActiveNetworkBandwidthComponent"/> or
/// <see cref="ActiveRouterTransferQueueComponent"/> during <see cref="Update"/>.
/// </summary>
public sealed partial class NetworkBandwidthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;

    [Dependency] private EntityQuery<NetworkBandwidthComponent> _bwQuery = default!;
    [Dependency] private EntityQuery<NetworkRouterComponent> _routerQuery = default!;
    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<ApcPowerReceiverComponent> _powerQuery = default!;
    [Dependency] private EntityQuery<DebugFallbackNetworkEntityComponent> _debugFallbackQuery = default!;

    private const float RouterLookupRange = 512f;
    private const int DefaultRouterFallbackBps = 65536;
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromSeconds(0.15);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        UpdateActiveBandwidthWindows(curTime);
        UpdateRouterTransferQueues(frameTime, curTime);
    }

    [SubscribeLocalEvent]
    private void OnBeforePacketSent(Entity<NetworkBandwidthComponent> ent, ref BeforePacketSentEvent args)
    {
        if (ent.Comp.MaxUploadBps <= 0 || ent.Comp.IsSaturated)
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnPacketReceived(Entity<NetworkBandwidthComponent> ent, ref DeviceNetworkPacketEvent<INetworkPayload> args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
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

        ActivateBandwidthWindow(uid, comp);

        var localAddr = "LOCAL";
        if (TryComp<DeviceNetworkComponent>(uid, out var netComp))
        {
            var addr = netComp.Address;
            localAddr = addr.ToString();
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

    private void UpdateActiveBandwidthWindows(TimeSpan curTime)
    {
        var query = EntityQueryEnumerator<ActiveNetworkBandwidthComponent, NetworkBandwidthComponent>();
        while (query.MoveNext(out var uid, out _, out var comp))
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

            if (comp.CurrentBytesReceived == 0 &&
                comp.CurrentBytesSent == 0 &&
                comp.CurrentQueuedPackets == 0 &&
                !comp.IsSaturated)
            {
                RemCompDeferred<ActiveNetworkBandwidthComponent>(uid);
            }
        }
    }

    private void UpdateRouterTransferQueues(float frameTime, TimeSpan curTime)
    {
        var queueQuery = EntityQueryEnumerator<ActiveRouterTransferQueueComponent, NetworkRouterComponent>();
        while (queueQuery.MoveNext(out var routerUid, out var queue, out var router))
        {
            if (queue.Transfers.Count == 0)
            {
                RemCompDeferred<ActiveRouterTransferQueueComponent>(routerUid);
                continue;
            }

            if (!IsRouterHostOperational(routerUid))
            {
                AbortAllTransfersOnRouter((routerUid, router), queue, "Station network router went offline.");
                RemCompDeferred<ActiveRouterTransferQueueComponent>(routerUid);
                continue;
            }

            var routerCompUid = UnwrapComputerEntity(routerUid);
            var routerMaxBps = _bwQuery.TryComp(routerCompUid, out var rBw) && rBw.MaxBps > 0
                ? rBw.MaxBps
                : DefaultRouterFallbackBps;
            var activeCount = Math.Max(1, queue.Transfers.Count);
            var routerShareBps = Math.Max(1, routerMaxBps / activeCount);

            var routerDirty = false;
            for (var i = queue.Transfers.Count - 1; i >= 0; i--)
            {
                var xfer = queue.Transfers[i];
                if (!ValidateTransferEndpoints(routerUid, router, xfer, out var failReason))
                {
                    queue.Transfers.RemoveAt(i);
                    routerDirty |= RemoveRouterConnectionRecord(router, xfer.ConnectionId);
                    xfer.OnAborted?.Invoke(xfer, failReason);
                    continue;
                }

                var pairRate = GetEffectiveTransferRateBps(xfer.SourceCompUid, xfer.TargetCompUid, routerUid);
                var effectiveRate = Math.Min(pairRate, routerShareBps);
                if (effectiveRate <= 0)
                {
                    queue.Transfers.RemoveAt(i);
                    routerDirty |= RemoveRouterConnectionRecord(router, xfer.ConnectionId);
                    xfer.OnAborted?.Invoke(xfer, "Transfer stalled: zero available bandwidth.");
                    continue;
                }

                xfer.FractionalBytes += effectiveRate * frameTime;
                var delta = (int) MathF.Floor(xfer.FractionalBytes);
                if (delta > 0)
                {
                    xfer.FractionalBytes -= delta;
                    var applied = Math.Min(delta, Math.Max(0, xfer.TotalBytes - xfer.TransferredBytes));
                    xfer.TransferredBytes += applied;

                    RecordDataTransmitted(xfer.SourceCompUid, applied, isUpload: true);
                    RecordDataTransmitted(xfer.TargetCompUid, applied, isUpload: false);
                    if (routerCompUid != xfer.SourceCompUid && routerCompUid != xfer.TargetCompUid)
                        RecordDataTransmitted(routerCompUid, applied, isUpload: false);
                }

                var pct = xfer.TotalBytes > 0 ? Math.Clamp(xfer.TransferredBytes * 100 / xfer.TotalBytes, 0, 100) : 100;
                UpdateRouterConnectionProgress(router, xfer, pct);

                if (xfer.TransferredBytes >= xfer.TotalBytes)
                {
                    queue.Transfers.RemoveAt(i);
                    routerDirty |= RemoveRouterConnectionRecord(router, xfer.ConnectionId);

                    if (xfer.PacketMetadata != null)
                        LogPacketToSpecificRouter((routerUid, router), xfer.PacketMetadata);

                    xfer.OnCompleted?.Invoke(xfer);
                }
                else if (curTime >= xfer.NextProgressUpdate)
                {
                    xfer.NextProgressUpdate = curTime + ProgressNotifyInterval;
                    routerDirty = true;
                    xfer.OnProgress?.Invoke(xfer);
                }
            }

            if (routerDirty)
                Dirty(routerUid, router);

            if (queue.Transfers.Count == 0)
                RemCompDeferred<ActiveRouterTransferQueueComponent>(routerUid);
        }
    }

    public bool EnqueueRouterTransfer(Entity<NetworkRouterComponent> router, QueuedRouterTransfer transfer)
    {
        if (!Exists(router.Owner) || !IsRouterHostOperational(router.Owner))
            return false;

        if (string.IsNullOrEmpty(transfer.TransferId))
            transfer.TransferId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        if (string.IsNullOrEmpty(transfer.ConnectionId))
            transfer.ConnectionId = $"XFER-{transfer.TransferId}";

        transfer.SourceCompUid = UnwrapComputerEntity(transfer.SourceCompUid);
        transfer.TargetCompUid = UnwrapComputerEntity(transfer.TargetCompUid);
        transfer.TotalBytes = Math.Max(64, transfer.TotalBytes);
        transfer.TransferredBytes = 0;
        transfer.FractionalBytes = 0f;
        transfer.NextProgressUpdate = _timing.CurTime + ProgressNotifyInterval;

        var queue = EnsureComp<ActiveRouterTransferQueueComponent>(router.Owner);
        queue.Transfers.Add(transfer);

        var srcGuid = _guid.GetGuid(transfer.SourceCompUid);
        var dstGuid = _guid.GetGuid(transfer.TargetCompUid);
        router.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == transfer.ConnectionId);
        router.Comp.ActiveConnections.Add(new ActiveNetworkConnectionRecord
        {
            ConnectionId = transfer.ConnectionId,
            SourceGuid = srcGuid,
            TargetGuid = dstGuid,
            ConnectionType = transfer.IsUpload ? "UPLOAD" : "DOWNLOAD",
            Description = $"{transfer.Description} (0%)"
        });
        Dirty(router);
        return true;
    }

    public bool CancelRouterTransferByConnectionId(Entity<NetworkRouterComponent> router, string connectionId, string reason = "Transfer cancelled.")
    {
        if (!TryComp<ActiveRouterTransferQueueComponent>(router.Owner, out var queue))
            return false;

        var cancelled = false;
        for (var i = queue.Transfers.Count - 1; i >= 0; i--)
        {
            var xfer = queue.Transfers[i];
            if (!string.Equals(xfer.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
                continue;

            queue.Transfers.RemoveAt(i);
            RemoveRouterConnectionRecord(router.Comp, xfer.ConnectionId);
            xfer.OnAborted?.Invoke(xfer, reason);
            cancelled = true;
        }

        if (cancelled)
            Dirty(router);

        if (queue.Transfers.Count == 0)
            RemCompDeferred<ActiveRouterTransferQueueComponent>(router.Owner);

        return cancelled;
    }

    public int CancelTransfersForProgram(EntityUid programUid, string reason = "Transfer cancelled.")
    {
        var count = 0;
        var query = EntityQueryEnumerator<ActiveRouterTransferQueueComponent, NetworkRouterComponent>();
        while (query.MoveNext(out var routerUid, out var queue, out var router))
        {
            var changed = false;
            for (var i = queue.Transfers.Count - 1; i >= 0; i--)
            {
                var xfer = queue.Transfers[i];
                if (xfer.InitiatorProgramUid != programUid)
                    continue;

                queue.Transfers.RemoveAt(i);
                RemoveRouterConnectionRecord(router, xfer.ConnectionId);
                xfer.OnAborted?.Invoke(xfer, reason);
                changed = true;
                count++;
            }

            if (changed)
                Dirty(routerUid, router);

            if (queue.Transfers.Count == 0)
                RemCompDeferred<ActiveRouterTransferQueueComponent>(routerUid);
        }

        return count;
    }

    public int CancelTransfersForGuid(Entity<NetworkRouterComponent> router, string guid, string reason = "Device blacklisted on router.")
    {
        if (!TryComp<ActiveRouterTransferQueueComponent>(router.Owner, out var queue))
            return 0;

        var clean = guid.Trim().ToUpperInvariant();
        var count = 0;
        for (var i = queue.Transfers.Count - 1; i >= 0; i--)
        {
            var xfer = queue.Transfers[i];
            var srcGuid = Exists(xfer.SourceCompUid) ? _guid.GetGuid(xfer.SourceCompUid) : string.Empty;
            var dstGuid = Exists(xfer.TargetCompUid) ? _guid.GetGuid(xfer.TargetCompUid) : string.Empty;
            if (!string.Equals(srcGuid, clean, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dstGuid, clean, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            queue.Transfers.RemoveAt(i);
            RemoveRouterConnectionRecord(router.Comp, xfer.ConnectionId);
            xfer.OnAborted?.Invoke(xfer, reason);
            count++;
        }

        if (count > 0)
            Dirty(router);

        if (queue.Transfers.Count == 0)
            RemCompDeferred<ActiveRouterTransferQueueComponent>(router.Owner);

        return count;
    }

    public bool HasActiveTransferForProgram(EntityUid programUid)
    {
        var query = EntityQueryEnumerator<ActiveRouterTransferQueueComponent>();
        while (query.MoveNext(out _, out var queue))
        {
            foreach (var xfer in queue.Transfers)
            {
                if (xfer.InitiatorProgramUid == programUid)
                    return true;
            }
        }

        return false;
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

        return StorePacket(uid, pkt, comp);
    }

    public NetworkDataPacket StorePacket(
        EntityUid uid,
        NetworkDataPacket packet,
        NetworkBandwidthComponent? comp = null)
    {
        var clone = packet.Clone();
        if (string.IsNullOrEmpty(clone.PacketId))
            clone.PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        if (clone.Timestamp == TimeSpan.Zero)
            clone.Timestamp = _timing.CurTime;

        if (!_bwQuery.Resolve(uid, ref comp, false))
            return clone;

        comp.StoredPackets.Add(clone);
        while (comp.StoredPackets.Count > comp.MaxStoredPackets)
        {
            comp.StoredPackets.RemoveAt(0);
        }

        Dirty(uid, comp);
        return clone;
    }

    public void LogPacketToRouters(NetworkDataPacket pkt, EntityUid? senderUid = null)
    {
        if (senderUid is { Valid: true } origin && Exists(origin))
        {
            var nearest = FindNearestOperationalRouter(origin);
            if (nearest != null)
            {
                LogPacketToSpecificRouter(nearest.Value, pkt);
                return;
            }
        }

        var routerQuery = EntityQueryEnumerator<NetworkRouterComponent>();
        while (routerQuery.MoveNext(out var rUid, out var netRouter))
        {
            if (!IsRouterHostOperational(rUid))
                continue;

            LogPacketToSpecificRouter((rUid, netRouter), pkt);
        }
    }

    public void LogPacketToSpecificRouter(Entity<NetworkRouterComponent> router, NetworkDataPacket pkt)
    {
        var routerCompUid = UnwrapComputerEntity(router.Owner);
        if (_bwQuery.TryComp(router.Owner, out var rBw))
            StorePacket(router.Owner, pkt, rBw);
        else if (_bwQuery.TryComp(routerCompUid, out var compBw))
            StorePacket(routerCompUid, pkt, compBw);

        router.Comp.RecordedPackets.Add(pkt.Clone());
        while (router.Comp.RecordedPackets.Count > router.Comp.MaxRecordedPackets)
        {
            router.Comp.RecordedPackets.RemoveAt(0);
        }

        Dirty(router);
    }

    public void RecordDataTransmitted(EntityUid uid, int byteCount, bool isUpload = false, NetworkBandwidthComponent? comp = null)
    {
        var compUid = UnwrapComputerEntity(uid);
        if (!_bwQuery.Resolve(compUid, ref comp, false))
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

        ActivateBandwidthWindow(compUid, comp);
        Dirty(compUid, comp);
    }

    public int GetEffectiveTransferRateBps(EntityUid senderUid, EntityUid receiverUid, EntityUid? explicitRouterHost = null)
    {
        var sComp = UnwrapComputerEntity(senderUid);
        var rComp = UnwrapComputerEntity(receiverUid);

        var up = _bwQuery.TryComp(sComp, out var sBw) && sBw.MaxUploadBps > 0 ? sBw.MaxUploadBps : 0;
        var down = _bwQuery.TryComp(rComp, out var rBw) && rBw.MaxBps > 0 ? rBw.MaxBps : 0;

        if (up <= 0 || down <= 0)
            return 0;

        var rate = Math.Min(up, down);

        EntityUid? routerHost = explicitRouterHost;
        if (routerHost == null)
        {
            var nearest = FindNearestOperationalRouter(sComp) ?? FindNearestOperationalRouter(rComp);
            routerHost = nearest?.Owner;
        }

        if (routerHost is { Valid: true } rHost)
        {
            var routerComp = UnwrapComputerEntity(rHost);
            if (_bwQuery.TryComp(routerComp, out var routerBw) && routerBw.MaxBps > 0)
                rate = Math.Min(rate, routerBw.MaxBps);
            else if (_bwQuery.TryComp(rHost, out var hostBw) && hostBw.MaxBps > 0)
                rate = Math.Min(rate, hostBw.MaxBps);
        }

        return rate;
    }

    public void RecordScreenSharingBandwidth(EntityUid targetUid, EntityUid? viewerTerminalUid = null, int byteCount = 2048)
    {
        var targetComp = UnwrapComputerEntity(targetUid);
        if (!_bwQuery.TryComp(targetComp, out var targetBw) || targetBw.IsSaturated)
            return;

        RecordDataTransmitted(targetComp, byteCount, isUpload: true, targetBw);

        if (viewerTerminalUid is { } term)
        {
            var viewerComp = UnwrapComputerEntity(term);
            if (_bwQuery.TryComp(viewerComp, out var viewerBw))
                RecordDataTransmitted(viewerComp, byteCount, isUpload: false, viewerBw);
        }
    }

    private void ActivateBandwidthWindow(EntityUid uid, NetworkBandwidthComponent comp)
    {
        if (comp.NextRateReset <= _timing.CurTime)
            comp.NextRateReset = _timing.CurTime + TimeSpan.FromSeconds(1);

        EnsureComp<ActiveNetworkBandwidthComponent>(uid);
    }

    private EntityUid UnwrapComputerEntity(EntityUid uid)
    {
        if (_hostQuery.TryComp(uid, out var host) && host.ComputerEntity is { } compUid && Exists(compUid))
            return compUid;

        return uid;
    }

    private bool IsRouterHostOperational(EntityUid routerHostOrComp)
    {
        if (!Exists(routerHostOrComp) || TerminatingOrDeleted(routerHostOrComp))
            return false;

        if (_powerQuery.TryComp(routerHostOrComp, out var apc) && !apc.Powered)
            return false;

        var routerComp = UnwrapComputerEntity(routerHostOrComp);
        if (_computerQuery.TryComp(routerComp, out var comp))
        {
            if (comp.NetworkWireCut || comp.PowerWireCut)
                return false;

            if (_statsQuery.TryComp(routerComp, out var stats) && !stats.HasNetworkCard)
                return false;
        }

        if (_bwQuery.TryComp(routerComp, out var bw) && bw.IsSaturated)
            return false;

        return true;
    }

    private Entity<NetworkRouterComponent>? FindNearestOperationalRouter(EntityUid origin)
    {
        if (!Exists(origin) || TerminatingOrDeleted(origin))
            return null;

        var originCoords = _transform.GetMapCoordinates(origin);
        Entity<NetworkRouterComponent>? best = null;
        var bestDist = float.MaxValue;

        if (originCoords.MapId != MapId.Nullspace)
        {
            var nearby = _lookup.GetEntitiesInRange<NetworkRouterComponent>(originCoords, RouterLookupRange, LookupFlags.All | LookupFlags.Approximate);
            foreach (var rEnt in nearby)
            {
                if (!IsRouterHostOperational(rEnt.Owner))
                    continue;

                var dist = (_transform.GetMapCoordinates(rEnt.Owner).Position - originCoords.Position).Length();
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = rEnt;
                }
            }
        }

        return best;
    }

    private bool ValidateTransferEndpoints(EntityUid routerUid, NetworkRouterComponent router, QueuedRouterTransfer xfer, out string reason)
    {
        if (!Exists(xfer.SourceCompUid) || TerminatingOrDeleted(xfer.SourceCompUid) ||
            !Exists(xfer.TargetCompUid) || TerminatingOrDeleted(xfer.TargetCompUid))
        {
            reason = "Transfer aborted: endpoint no longer exists.";
            return false;
        }

        if (!IsEndpointActive(xfer.SourceCompUid, xfer.AllowDebugFallback) ||
            !IsEndpointActive(xfer.TargetCompUid, xfer.AllowDebugFallback))
        {
            reason = "Transfer aborted: endpoint lost power or network card.";
            return false;
        }

        var srcGuid = _guid.GetGuid(xfer.SourceCompUid);
        var dstGuid = _guid.GetGuid(xfer.TargetCompUid);
        if (router.BlacklistedGuids.Contains(srcGuid) || router.BlacklistedGuids.Contains(dstGuid))
        {
            reason = "Transfer aborted: device blacklisted on station router.";
            return false;
        }

        if (xfer.InitiatorProgramUid is { } progUid && (!Exists(progUid) || TerminatingOrDeleted(progUid)))
        {
            reason = "Transfer aborted: initiating program terminated.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool IsEndpointActive(EntityUid compUid, bool allowDebugFallback)
    {
        if (allowDebugFallback && _debugFallbackQuery.HasComp(compUid))
            return true;

        if (_computerQuery.TryComp(compUid, out var comp))
        {
            if (comp.NetworkWireCut || comp.PowerWireCut)
                return false;

            if (comp.RequirePowerToBoot && !comp.IsPowered)
                return false;
        }

        if (_statsQuery.TryComp(compUid, out var stats) && !stats.HasNetworkCard)
            return false;

        return true;
    }

    private void AbortAllTransfersOnRouter(Entity<NetworkRouterComponent> router, ActiveRouterTransferQueueComponent queue, string reason)
    {
        var copy = queue.Transfers.ToList();
        queue.Transfers.Clear();
        foreach (var xfer in copy)
        {
            RemoveRouterConnectionRecord(router.Comp, xfer.ConnectionId);
            xfer.OnAborted?.Invoke(xfer, reason);
        }
        Dirty(router);
    }

    private static bool RemoveRouterConnectionRecord(NetworkRouterComponent router, string connectionId)
    {
        return router.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0;
    }

    private static void UpdateRouterConnectionProgress(NetworkRouterComponent router, QueuedRouterTransfer xfer, int pct)
    {
        foreach (var rec in router.ActiveConnections)
        {
            if (rec.ConnectionId == xfer.ConnectionId)
            {
                rec.Description = $"{xfer.Description} ({pct}%)";
                return;
            }
        }
    }
}
