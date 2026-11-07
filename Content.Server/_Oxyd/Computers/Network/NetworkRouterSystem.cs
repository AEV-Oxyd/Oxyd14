using Content.Server._Oxyd.Computers.Cartridges;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

public sealed class NetworkRouterSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private readonly ServerRemoteScreenSystem _remoteScreen = default!;
    [Dependency] private readonly SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<DeviceGuidComponent> _guidQuery;
    private EntityQuery<OxydComputerComponent> _computerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _guidQuery = GetEntityQuery<DeviceGuidComponent>();
        _computerQuery = GetEntityQuery<OxydComputerComponent>();

        SubscribeLocalEvent<NetworkRouterComponent, MapInitEvent>(OnRouterMapInit);
        SubscribeLocalEvent<DataServerComponent, MapInitEvent>(OnDataServerMapInit);

        SubscribeLocalEvent<RouterCartridgeComponent, CartridgeUiReadyEvent>(OnRouterUiReady);
        SubscribeLocalEvent<RouterCartridgeComponent, CartridgeMessageEvent>(OnRouterUiMessage);

        SubscribeLocalEvent<PacketSnifferCartridgeComponent, CartridgeUiReadyEvent>(OnSnifferUiReady);
        SubscribeLocalEvent<PacketSnifferCartridgeComponent, CartridgeMessageEvent>(OnSnifferUiMessage);
    }

    private void OnRouterMapInit(Entity<NetworkRouterComponent> ent, ref MapInitEvent args)
    {
        RefreshRouterDevices(ent);
    }

    private void OnDataServerMapInit(Entity<DataServerComponent> ent, ref MapInitEvent args)
    {
        var guid = EnsureDeviceGuid(ent.Owner);
        var router = FindNearestRouter(ent.Owner);
        if (router == null)
            return;

        RegisterDeviceOnRouter(router.Value, guid);

        foreach (var proto in ent.Comp.ServerProtocols)
        {
            router.Value.Comp.DnsRecords[proto] = guid;
            if (proto.Contains("Manifest", StringComparison.OrdinalIgnoreCase))
            {
                router.Value.Comp.DnsRecords["Crew manifest server"] = guid;
                router.Value.Comp.DnsRecords["crew_manifest"] = guid;
            }
        }

        router.Value.Comp.DnsRecords[Name(ent.Owner)] = guid;
        Dirty(router.Value);
    }

    public string EnsureDeviceGuid(EntityUid uid)
    {
        if (_guidQuery.TryGetComponent(uid, out var guidComp))
        {
            _guidSystem.EnsureGuid(uid, guidComp);
            return guidComp.Guid;
        }

        var comp = EnsureComp<DeviceGuidComponent>(uid);
        _guidSystem.EnsureGuid(uid, comp);
        return comp.Guid;
    }

    public bool TryFindDeviceByGuid(string guid, out EntityUid foundUid)
    {
        foundUid = EntityUid.Invalid;
        if (string.IsNullOrWhiteSpace(guid))
            return false;

        if (_guidSystem.TryGetEntityByGuid(guid, out var target) && target.HasValue)
        {
            foundUid = target.Value;
            return true;
        }

        var netQuery = EntityQueryEnumerator<DeviceNetworkComponent>();
        while (netQuery.MoveNext(out var uid, out var netComp))
        {
            var addr = netComp.Address;
            if (string.Equals(addr.ToString(), guid, StringComparison.OrdinalIgnoreCase))
            {
                foundUid = uid;
                return true;
            }
        }

        return false;
    }

    public Entity<NetworkRouterComponent>? FindNearestRouter(EntityUid origin)
    {
        var originXform = Transform(origin);
        Entity<NetworkRouterComponent>? bestRouter = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<NetworkRouterComponent, TransformComponent>();
        while (query.MoveNext(out var routerUid, out var routerComp, out var routerXform))
        {
            if (_computerQuery.TryGetComponent(routerUid, out var oxydComp) &&
                (oxydComp.NetworkWireCut || oxydComp.PowerWireCut))
                continue;

            if (routerXform.MapID != originXform.MapID)
                continue;

            var dist = (_transform.GetWorldPosition(routerXform) - _transform.GetWorldPosition(originXform)).LengthSquared();
            if (dist < bestDist)
            {
                bestDist = dist;
                bestRouter = (routerUid, routerComp);
            }
        }

        if (bestRouter == null)
        {
            var devRouterQuery = EntityQueryEnumerator<DeviceNetworkRouterComponent, TransformComponent>();
            while (devRouterQuery.MoveNext(out var rUid, out _, out var rXform))
            {
                if (rXform.MapID != originXform.MapID)
                    continue;
                var added = EnsureComp<NetworkRouterComponent>(rUid);
                bestRouter = (rUid, added);
                break;
            }
        }

        return bestRouter;
    }

    public bool IsNetworkConnected(EntityUid uid)
    {
        if (_computerQuery.TryGetComponent(uid, out var comp) && (comp.NetworkWireCut || comp.PowerWireCut))
            return false;

        var router = FindNearestRouter(uid);
        if (router == null)
            return false;

        var guid = EnsureDeviceGuid(uid);
        return !router.Value.Comp.BlacklistedGuids.Contains(guid);
    }

    public bool IsGuidBlacklisted(EntityUid contextUid, string guid)
    {
        var router = FindNearestRouter(contextUid);
        if (router == null)
            return false;

        return router.Value.Comp.BlacklistedGuids.Contains(guid);
    }

    public string? ResolveDns(EntityUid origin, string serviceName)
    {
        if (!IsNetworkConnected(origin))
            return null;

        var router = FindNearestRouter(origin);
        if (router == null)
            return null;

        RefreshRouterDevices(router.Value);

        if (router.Value.Comp.DnsRecords.TryGetValue(serviceName, out var targetGuid))
        {
            if (!router.Value.Comp.BlacklistedGuids.Contains(targetGuid))
                return targetGuid;
        }

        foreach (var (key, val) in router.Value.Comp.DnsRecords)
        {
            if (key.Equals(serviceName, StringComparison.OrdinalIgnoreCase) &&
                !router.Value.Comp.BlacklistedGuids.Contains(val))
            {
                return val;
            }
        }

        return null;
    }

    public void RegisterDeviceOnRouter(Entity<NetworkRouterComponent> router, string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return;

        if (router.Comp.ConnectedDevices.Add(guid))
        {
            AddRouterLog(router, $"Device connected: {guid}");
            Dirty(router);
        }
    }

    public void RefreshRouterDevices(Entity<NetworkRouterComponent> router)
    {
        var routerMap = Transform(router.Owner).MapID;
        var guidEnumerator = EntityQueryEnumerator<DeviceGuidComponent, TransformComponent>();
        while (guidEnumerator.MoveNext(out var uid, out var guidComp, out var xform))
        {
            if (xform.MapID != routerMap || string.IsNullOrEmpty(guidComp.Guid))
                continue;

            router.Comp.ConnectedDevices.Add(guidComp.Guid);

            if (TryComp<DataServerComponent>(uid, out var server))
            {
                router.Comp.DnsRecords[Name(uid)] = guidComp.Guid;
                foreach (var proto in server.ServerProtocols)
                {
                    router.Comp.DnsRecords[proto] = guidComp.Guid;
                    if (proto.Contains("Manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        router.Comp.DnsRecords["Crew manifest server"] = guidComp.Guid;
                        router.Comp.DnsRecords["crew_manifest"] = guidComp.Guid;
                    }
                }
            }
        }

        if (TryComp<NetworkBandwidthComponent>(router.Owner, out var bw))
        {
            foreach (var pkt in bw.StoredPackets)
            {
                if (!router.Comp.RecordedPackets.Contains(pkt))
                {
                    if (router.Comp.RecordedPackets.Count >= router.Comp.MaxRecordedPackets)
                        router.Comp.RecordedPackets.RemoveAt(0);
                    router.Comp.RecordedPackets.Add(pkt);
                }
            }
        }
    }

    public bool RecordAndValidatePacket(EntityUid sender, EntityUid target, NetworkDataPacket packet)
    {
        if (_computerQuery.TryGetComponent(sender, out var senderComp) && (senderComp.NetworkWireCut || senderComp.PowerWireCut))
            return false;

        if (_computerQuery.TryGetComponent(target, out var targetComp) && (targetComp.NetworkWireCut || targetComp.PowerWireCut))
            return false;

        var router = FindNearestRouter(sender) ?? FindNearestRouter(target);
        if (router == null)
            return true;

        RegisterDeviceOnRouter(router.Value, packet.SourceAddress);
        RegisterDeviceOnRouter(router.Value, packet.TargetAddress);

        if (router.Value.Comp.BlacklistedGuids.Contains(packet.SourceAddress) ||
            router.Value.Comp.BlacklistedGuids.Contains(packet.TargetAddress))
        {
            AddRouterLog(router.Value, $"BLOCKED packet #{packet.PacketId} ({packet.Protocol}) {packet.SourceAddress} -> {packet.TargetAddress} [BLACKLISTED]");
            return false;
        }

        if (router.Value.Comp.RecordedPackets.Count >= router.Value.Comp.MaxRecordedPackets)
            router.Value.Comp.RecordedPackets.RemoveAt(0);

        router.Value.Comp.RecordedPackets.Add(packet);
        AddRouterLog(router.Value, $"PKT #{packet.PacketId} [{packet.Protocol}] {packet.SourceAddress} -> {packet.TargetAddress} ({packet.ByteSize}B, {(packet.IsEncrypted ? "ENC" : "PLAIN")})");
        RefreshAllRouterAndSnifferUis(router.Value);
        return true;
    }

    public string RegisterActiveConnection(EntityUid origin, string sourceGuid, string targetGuid, string connectionType, string description)
    {
        var connId = $"{connectionType}-{sourceGuid}-{targetGuid}";
        var router = FindNearestRouter(origin);
        if (router == null)
            return connId;

        router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connId);
        router.Value.Comp.ActiveConnections.Add(new ActiveNetworkConnectionRecord
        {
            ConnectionId = connId,
            SourceGuid = sourceGuid,
            TargetGuid = targetGuid,
            ConnectionType = connectionType,
            Description = description
        });

        AddRouterLog(router.Value, $"OPEN [{connectionType}] {sourceGuid} -> {targetGuid} ({description})");
        RefreshAllRouterAndSnifferUis(router.Value);
        return connId;
    }

    public void UnregisterActiveConnection(EntityUid origin, string connectionId)
    {
        var router = FindNearestRouter(origin);
        if (router == null)
            return;

        if (router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0)
        {
            AddRouterLog(router.Value, $"CLOSE {connectionId}");
            RefreshAllRouterAndSnifferUis(router.Value);
        }
    }

    public void CloseConnectionById(Entity<NetworkRouterComponent> router, string connectionId)
    {
        var conn = router.Comp.ActiveConnections.Find(c => c.ConnectionId == connectionId);
        if (conn == null)
            return;

        router.Comp.ActiveConnections.Remove(conn);
        AddRouterLog(router, $"TERMINATED connection {conn.ConnectionId} ({conn.SourceGuid} -> {conn.TargetGuid})");

        TerminateUnderlyingConnection(conn.SourceGuid, conn.TargetGuid, conn.ConnectionType);
        RefreshAllRouterAndSnifferUis(router);
    }

    public void BlacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return;

        guid = guid.Trim();
        router.Comp.BlacklistedGuids.Add(guid);
        AddRouterLog(router, $"BLACKLISTED GUID: {guid} - severing all active connections.");

        var toClose = new List<ActiveNetworkConnectionRecord>();
        foreach (var conn in router.Comp.ActiveConnections)
        {
            if (string.Equals(conn.SourceGuid, guid, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(conn.TargetGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                toClose.Add(conn);
            }
        }

        foreach (var conn in toClose)
        {
            router.Comp.ActiveConnections.Remove(conn);
            TerminateUnderlyingConnection(conn.SourceGuid, conn.TargetGuid, conn.ConnectionType);
        }

        TerminateAllSessionsForGuid(guid);

        Dirty(router);
        RefreshAllRouterAndSnifferUis(router);
    }

    public void UnblacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        if (router.Comp.BlacklistedGuids.Remove(guid.Trim()))
        {
            AddRouterLog(router, $"UNBLACKLISTED GUID: {guid}");
            Dirty(router);
            RefreshAllRouterAndSnifferUis(router);
        }
    }

    public void TerminateAllSessionsForGuid(string guid)
    {
        var termQuery = EntityQueryEnumerator<TerminalCartridgeComponent, CartridgeComponent>();
        while (termQuery.MoveNext(out var termUid, out var termComp, out var cart))
        {
            if (string.Equals(termComp.ConnectedRemoteGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                termComp.ConnectedRemoteGuid = null;
                termComp.ConnectedRemoteUser = null;
                termComp.IsScreenAttached = false;
                termComp.History.Add($"[Connection to {guid} forcibly closed by router]");
                Dirty(termUid, termComp);
                _remoteScreen.DetachViewer(termUid);
                if (cart.LoaderUid is { } lUid)
                    Get<TerminalCartridgeSystem>().UpdateUiState(termUid, lUid, termComp);
            }
            else if (cart.LoaderUid is { } loaderUid)
            {
                var loaderGuid = EnsureDeviceGuid(loaderUid);
                if (string.Equals(loaderGuid, guid, StringComparison.OrdinalIgnoreCase) && termComp.ConnectedRemoteGuid != null)
                {
                    termComp.ConnectedRemoteGuid = null;
                    termComp.ConnectedRemoteUser = null;
                    termComp.IsScreenAttached = false;
                    termComp.History.Add("[Connection forcibly closed: local GUID blacklisted]");
                    Dirty(termUid, termComp);
                    _remoteScreen.DetachViewer(termUid);
                    Get<TerminalCartridgeSystem>().UpdateUiState(termUid, loaderUid, termComp);
                }
            }
        }
    }

    private void TerminateUnderlyingConnection(string sourceGuid, string targetGuid, string connectionType)
    {
        TerminateAllSessionsForGuid(sourceGuid);
        TerminateAllSessionsForGuid(targetGuid);
    }

    private void AddRouterLog(Entity<NetworkRouterComponent> router, string entry)
    {
        if (router.Comp.ConnectionLogs.Count >= router.Comp.MaxConnectionLogs)
            router.Comp.ConnectionLogs.RemoveAt(0);

        router.Comp.ConnectionLogs.Add(entry);
    }

    private void OnRouterUiReady(Entity<RouterCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateRouterUi(ent.Owner, args.Loader);
    }

    private void OnRouterUiMessage(Entity<RouterCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not RouterUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var router = EnsureRouterForLoader(loaderUid);
        if (router == null)
            return;

        switch (msg.Action)
        {
            case RouterUiAction.Refresh:
                RefreshRouterDevices(router.Value);
                break;
            case RouterUiAction.CloseConnection:
                CloseConnectionById(router.Value, msg.TargetId);
                break;
            case RouterUiAction.BlacklistGuid:
                BlacklistGuid(router.Value, msg.TargetId);
                break;
            case RouterUiAction.UnblacklistGuid:
                UnblacklistGuid(router.Value, msg.TargetId);
                break;
            case RouterUiAction.RegisterDns:
                if (!string.IsNullOrWhiteSpace(msg.TargetId) && !string.IsNullOrWhiteSpace(msg.ExtraValue))
                {
                    router.Value.Comp.DnsRecords[msg.TargetId.Trim()] = msg.ExtraValue.Trim();
                    AddRouterLog(router.Value, $"DNS: '{msg.TargetId.Trim()}' -> {msg.ExtraValue.Trim()}");
                }
                break;
            case RouterUiAction.ClearLogs:
                router.Value.Comp.ConnectionLogs.Clear();
                router.Value.Comp.RecordedPackets.Clear();
                break;
        }

        UpdateRouterUi(ent.Owner, loaderUid);
    }

    private Entity<NetworkRouterComponent>? EnsureRouterForLoader(EntityUid loaderUid)
    {
        if (TryComp<NetworkRouterComponent>(loaderUid, out var ownRouter))
            return (loaderUid, ownRouter);

        return FindNearestRouter(loaderUid);
    }

    public void UpdateRouterUi(EntityUid cartridgeUid, EntityUid loaderUid)
    {
        var router = EnsureRouterForLoader(loaderUid);
        if (router == null)
            return;

        RefreshRouterDevices(router.Value);
        var routerGuid = EnsureDeviceGuid(router.Value.Owner);

        var activeConns = new List<RouterConnectionView>();
        foreach (var c in router.Value.Comp.ActiveConnections)
        {
            activeConns.Add(new RouterConnectionView
            {
                ConnectionId = c.ConnectionId,
                SourceGuid = c.SourceGuid,
                TargetGuid = c.TargetGuid,
                ConnectionType = c.ConnectionType,
                Description = c.Description
            });
        }

        var packetViews = new List<RouterPacketLogView>();
        uint idx = 1;
        foreach (var pkt in router.Value.Comp.RecordedPackets)
        {
            packetViews.Add(new RouterPacketLogView
            {
                PacketId = idx++,
                SenderGuid = pkt.SourceAddress,
                TargetGuid = pkt.TargetAddress,
                PacketType = pkt.Protocol,
                IsEncrypted = pkt.IsEncrypted,
                DecipheredSummary = GetPacketReadableSummary(pkt)
            });
        }

        var state = new RouterUiState(
            routerGuid,
            activeConns,
            new List<string>(router.Value.Comp.ConnectedDevices),
            new List<string>(router.Value.Comp.BlacklistedGuids),
            packetViews,
            new List<string>(router.Value.Comp.ConnectionLogs),
            new Dictionary<string, string>(router.Value.Comp.DnsRecords));

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private void OnSnifferUiReady(Entity<PacketSnifferCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateSnifferUi(ent, args.Loader, "Packet capture ready.");
    }

    private void OnSnifferUiMessage(Entity<PacketSnifferCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not PacketSnifferUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var router = EnsureRouterForLoader(loaderUid);
        var status = string.Empty;

        switch (msg.Action)
        {
            case PacketSnifferUiAction.ToggleCapture:
                ent.Comp.IsCapturing = !ent.Comp.IsCapturing;
                status = ent.Comp.IsCapturing ? "Capture enabled." : "Capture paused.";
                break;

            case PacketSnifferUiAction.Refresh:
                status = "Refreshed captured packets.";
                break;

            case PacketSnifferUiAction.Clear:
                if (router != null)
                    router.Value.Comp.RecordedPackets.Clear();
                status = "Cleared packet buffer.";
                break;

            case PacketSnifferUiAction.ReemitPacket:
                if (router == null)
                {
                    status = "Error: No router reachable.";
                    break;
                }

                RefreshRouterDevices(router.Value);
                var pktIndex = (int) msg.PacketId - 1;
                if (pktIndex < 0 || pktIndex >= router.Value.Comp.RecordedPackets.Count)
                {
                    status = $"Packet #{msg.PacketId} not found.";
                    break;
                }

                var original = router.Value.Comp.RecordedPackets[pktIndex];
                var targetGuid = string.IsNullOrWhiteSpace(msg.TargetGuid) ? original.TargetAddress : msg.TargetGuid.Trim();
                if (!TryFindDeviceByGuid(targetGuid, out var targetUid))
                {
                    status = $"Target GUID '{targetGuid}' not found on network.";
                    break;
                }

                var newRaw = original.RawPayload;
                var newSummary = original.PayloadSummary;
                if (!original.IsEncrypted && !string.IsNullOrEmpty(msg.ModifiedPayload))
                {
                    newRaw = msg.ModifiedPayload;
                    newSummary = $"[RE-EMITTED] {msg.ModifiedPayload}";
                }

                var reemitted = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8],
                    SourceAddress = EnsureDeviceGuid(loaderUid),
                    TargetAddress = targetGuid,
                    Protocol = original.Protocol,
                    PayloadSummary = newSummary,
                    RawPayload = newRaw,
                    IsEncrypted = original.IsEncrypted,
                    ByteSize = Math.Max(64, newRaw.Length),
                    Timestamp = _timing.CurTime
                };

                _bandwidth.RecordDataTransmitted(loaderUid, reemitted.ByteSize);
                RecordAndValidatePacket(loaderUid, targetUid, reemitted);

                status = original.IsEncrypted
                    ? $"Re-emitted encrypted packet #{msg.PacketId} to {targetGuid} (payload unmodified)."
                    : $"Re-emitted packet #{msg.PacketId} to {targetGuid} with payload: '{newRaw}'.";
                break;
        }

        UpdateSnifferUi(ent, loaderUid, status);
    }

    private void UpdateSnifferUi(Entity<PacketSnifferCartridgeComponent> ent, EntityUid loaderUid, string status)
    {
        var router = EnsureRouterForLoader(loaderUid);
        var views = new List<CapturedPacketView>();

        if (router != null && ent.Comp.IsCapturing)
        {
            RefreshRouterDevices(router.Value);
            uint idx = 1;
            foreach (var pkt in router.Value.Comp.RecordedPackets)
            {
                views.Add(new CapturedPacketView
                {
                    PacketId = idx++,
                    SenderGuid = pkt.SourceAddress,
                    TargetGuid = pkt.TargetAddress,
                    PacketType = pkt.Protocol,
                    IsEncrypted = pkt.IsEncrypted,
                    PayloadPreview = GetPacketReadableSummary(pkt),
                    Parameters = new Dictionary<string, string>(pkt.Parameters)
                });
            }
        }

        var state = new PacketSnifferUiState(ent.Comp.IsCapturing, views, status);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    public static string GetPacketReadableSummary(NetworkDataPacket pkt)
    {
        if (pkt.IsEncrypted)
        {
            var hash = Math.Abs((pkt.RawPayload ?? pkt.Protocol).GetHashCode());
            return $"[ENCRYPTED AES-256: 0x{hash:X8}{(hash ^ 0x5A5A5A5A):X8}]";
        }

        if (!string.IsNullOrEmpty(pkt.PayloadSummary))
            return pkt.PayloadSummary;

        return !string.IsNullOrEmpty(pkt.RawPayload) ? pkt.RawPayload : pkt.Protocol;
    }

    private void RefreshAllRouterAndSnifferUis(Entity<NetworkRouterComponent> router)
    {
        var routerCarts = EntityQueryEnumerator<RouterCartridgeComponent, CartridgeComponent>();
        while (routerCarts.MoveNext(out var uid, out _, out var cart))
        {
            if (cart.LoaderUid is { } loaderUid)
                UpdateRouterUi(uid, loaderUid);
        }

        var snifferCarts = EntityQueryEnumerator<PacketSnifferCartridgeComponent, CartridgeComponent>();
        while (snifferCarts.MoveNext(out var uid, out var sniffer, out var cart))
        {
            if (cart.LoaderUid is { } loaderUid)
                UpdateSnifferUi((uid, sniffer), loaderUid, string.Empty);
        }
    }
}
