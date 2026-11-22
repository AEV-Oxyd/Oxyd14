using System.Linq;
using Content.Server._Oxyd.Computers.Programs;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server._Oxyd.Computers.Servers;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.GameTicking;
using Content.Shared.Power;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Network;

/// <summary>
/// Marker component identifying entities spawned solely as the #if DEBUG global network fallback.
/// </summary>
[RegisterComponent]
public sealed partial class DebugFallbackNetworkEntityComponent : Component
{
    [DataField]
    public bool IsServer;
}

public sealed partial class NetworkRouterSystem : EntitySystem
{
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
#if DEBUG
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MetaDataSystem _meta = default!;
#endif

    private EntityQuery<OxydComputerComponent> _computerQuery;
    private EntityQuery<ComputerStatsComponent> _statsQuery;
    private EntityQuery<EmbeddedComputerHostComponent> _hostQuery;
    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<NetworkRouterComponent> _routerQuery;
    private EntityQuery<DebugFallbackNetworkEntityComponent> _debugFallbackQuery;

#if DEBUG
    private static readonly EntProtoId DebugRouterPrototype = "StationNetworkRouter";

    public const string DebugServerDisplayName = "DEV DEBUG SERVER";
#endif

    private const float RouterLookupRange = 512f;

#if DEBUG
    private EntityUid _debugRouter;
    private bool _ensuringDebugRouter;
    private bool _flushingEntities;
#endif

    public override void Initialize()
    {
        base.Initialize();

        _computerQuery = GetEntityQuery<OxydComputerComponent>();
        _statsQuery = GetEntityQuery<ComputerStatsComponent>();
        _hostQuery = GetEntityQuery<EmbeddedComputerHostComponent>();
        _programQuery = GetEntityQuery<ProgramComponent>();
        _routerQuery = GetEntityQuery<NetworkRouterComponent>();
        _debugFallbackQuery = GetEntityQuery<DebugFallbackNetworkEntityComponent>();

        SubscribeLocalEvent<NetworkRouterComponent, MapInitEvent>(OnRouterMapInit);
        SubscribeLocalEvent<NetworkRouterComponent, PowerChangedEvent>(OnRouterPowerChanged);
#if DEBUG
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        EntityManager.BeforeEntityFlush += OnBeforeEntityFlush;
        EntityManager.AfterEntityFlush += OnAfterEntityFlush;
#endif

        SubscribeLocalEvent<RouterProgramComponent, ProgramStartupEvent>(OnRouterProgramStartup);
        SubscribeLocalEvent<RouterProgramComponent, ProgramActivatedEvent>(OnRouterProgramActivated);
        Subs.BuiEvents<RouterProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<RouterActionMessage>(OnRouterUiMessage);
        });

        SubscribeLocalEvent<PacketSnifferProgramComponent, ProgramStartupEvent>(OnSnifferStartup);
        SubscribeLocalEvent<PacketSnifferProgramComponent, ProgramActivatedEvent>(OnSnifferActivated);
        Subs.BuiEvents<PacketSnifferProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<PacketSnifferActionMessage>(OnSnifferUiMessage);
        });
    }

#if DEBUG
    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.BeforeEntityFlush -= OnBeforeEntityFlush;
        EntityManager.AfterEntityFlush -= OnAfterEntityFlush;
    }

    private void OnBeforeEntityFlush()
    {
        _flushingEntities = true;
        _debugRouter = EntityUid.Invalid;
        _ensuringDebugRouter = false;
    }

    private void OnAfterEntityFlush()
    {
        _flushingEntities = false;
    }
#endif

    private void OnRouterMapInit(Entity<NetworkRouterComponent> ent, ref MapInitEvent args)
    {
        EnsureDeviceGuid(ent.Owner);
        RefreshDnsDirectory(ent);
    }

    private void OnRouterPowerChanged(Entity<NetworkRouterComponent> ent, ref PowerChangedEvent args)
    {
#if DEBUG
        if (_flushingEntities)
            return;
#endif
        if (TerminatingOrDeleted(ent.Owner))
            return;

        RefreshAllRouterProgramUis();
    }

#if DEBUG
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _debugRouter = EntityUid.Invalid;
        _ensuringDebugRouter = false;
    }

    /// <summary>
    /// Returns true if any non-debug DataServer entity exists and has an operational network interface.
    /// </summary>
    public bool HasOperationalRealCentralServer()
    {
        var query = EntityQueryEnumerator<DataServerComponent>();
        while (query.MoveNext(out var sUid, out _))
        {
            if (_debugFallbackQuery.HasComp(sUid) || TerminatingOrDeleted(sUid))
                continue;

            var sComp = UnwrapComputerEntity(sUid);
            if (IsRouterOperational(sUid) && HasActiveNetworkCard(sComp))
                return true;
        }

        return false;
    }

    private bool TryEnsureDebugGlobalRouter(out Entity<NetworkRouterComponent> router)
    {
        router = default;
        if (_ensuringDebugRouter || _flushingEntities)
            return false;

        _ensuringDebugRouter = true;
        try
        {
            if (!Exists(_debugRouter) || TerminatingOrDeleted(_debugRouter))
            {
                _debugRouter = Spawn(DebugRouterPrototype, MapCoordinates.Nullspace);
                _meta.SetEntityName(_debugRouter, DebugServerDisplayName);
                _power.SetNeedsPower(_debugRouter, false);
                var marker = EnsureComp<DebugFallbackNetworkEntityComponent>(_debugRouter);
                marker.IsServer = false;
            }

            var compSys = EntityManager.System<OxydComputerSystem>();
            if (TryComp<EmbeddedComputerHostComponent>(_debugRouter, out var host) &&
                compSys.EnsureComputerBoundToHost((_debugRouter, host), out var routerCompUid) &&
                _computerQuery.TryComp(routerCompUid, out var comp))
            {
                _meta.SetEntityName(routerCompUid, DebugServerDisplayName);
                var compMarker = EnsureComp<DebugFallbackNetworkEntityComponent>(routerCompUid);
                compMarker.IsServer = false;
                comp.IsPowered = true;
                Dirty(routerCompUid, comp);
                compSys.RecalculateHardwareStats(routerCompUid, comp);
                if (!comp.IsBooted)
                    compSys.RefreshComputerBootState(routerCompUid, comp);
            }

            EntityManager.System<DataServerSystem>().EnsureDebugRepositoryServer();

            if (!_routerQuery.TryComp(_debugRouter, out var rComp))
                return false;

            router = (_debugRouter, rComp);
            RefreshDnsDirectory(router);
            return true;
        }
        finally
        {
            _ensuringDebugRouter = false;
        }
    }
#endif

#if DEBUG
    private void EnsureDebugServerIfNeeded(EntityUid context)
    {
        if (_flushingEntities ||
            _ensuringDebugRouter ||
            LifeStage(context) != EntityLifeStage.MapInitialized ||
            TerminatingOrDeleted(context) ||
            HasOperationalRealCentralServer())
        {
            return;
        }

        _ensuringDebugRouter = true;
        try
        {
            EntityManager.System<DataServerSystem>().EnsureDebugRepositoryServer();
        }
        finally
        {
            _ensuringDebugRouter = false;
        }
    }
#endif

    public bool IsUsingDebugFallbackRouter(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        return router.Owner == _debugRouter || _debugFallbackQuery.HasComp(router.Owner);
#else
        return false;
#endif
    }

    /// <summary>
    /// Returns a clear network/server banner label when a device connects through the
    /// #if DEBUG global fallback network (when no central server is found).
    /// </summary>
    public string? GetConnectedNetworkLabel(EntityUid origin)
    {
#if DEBUG
        var compUid = UnwrapComputerEntity(origin);
        if (!HasActiveNetworkCard(compUid))
            return null;

        var router = FindNearestRouter(compUid);
        if (router == null)
            return null;

        if (IsUsingDebugFallbackRouter(router.Value) || !HasOperationalRealCentralServer())
            return DebugServerDisplayName;
#endif
        return null;
    }

    private void OnRouterProgramStartup(Entity<RouterProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateRouterProgramUi(ent.Owner);
    }

    private void OnRouterProgramActivated(Entity<RouterProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateRouterProgramUi(ent.Owner);
    }

    private void OnSnifferStartup(Entity<PacketSnifferProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdatePacketSnifferUi(ent.Owner);
    }

    private void OnSnifferActivated(Entity<PacketSnifferProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdatePacketSnifferUi(ent.Owner);
    }

    private void OnRouterUiMessage(Entity<RouterProgramComponent> ent, ref RouterActionMessage msg)
    {
        var compUid = ResolveComputerFromProgram(ent.Owner);
        var router = FindActiveRouter(compUid);
        if (router == null)
        {
            ent.Comp.StatusMessage = "ERROR: No active Network Router found.";
            UpdateRouterProgramUi(ent.Owner);
            return;
        }

        switch (msg.Action)
        {
            case RouterUiAction.Refresh:
                RefreshDnsDirectory(router.Value);
                RefreshRouterDevices(router.Value);
                ent.Comp.StatusMessage = "Router state synchronized.";
                break;

            case RouterUiAction.KillConnection:
                CloseConnectionById(router.Value, msg.TargetId);
                ent.Comp.StatusMessage = $"Closed connection {msg.TargetId}.";
                break;

            case RouterUiAction.BlacklistGuid:
                BlacklistGuid(router.Value, msg.TargetId);
                ent.Comp.StatusMessage = $"Blacklisted GUID {msg.TargetId}.";
                break;

            case RouterUiAction.UnblacklistGuid:
                UnblacklistGuid(router.Value, msg.TargetId);
                ent.Comp.StatusMessage = $"Removed GUID {msg.TargetId} from blacklist.";
                break;

            case RouterUiAction.SetDns:
                if (!string.IsNullOrWhiteSpace(msg.TargetId) && !string.IsNullOrWhiteSpace(msg.Value))
                {
                    router.Value.Comp.DnsRecords[msg.TargetId.Trim()] = msg.Value.Trim().ToUpperInvariant();
                    Dirty(router.Value);
                    ent.Comp.StatusMessage = $"DNS set: {msg.TargetId} -> {msg.Value}";
                }
                break;

            case RouterUiAction.RemoveDns:
                if (router.Value.Comp.DnsRecords.Remove(msg.TargetId.Trim()))
                {
                    Dirty(router.Value);
                    ent.Comp.StatusMessage = $"DNS entry '{msg.TargetId}' removed.";
                }
                break;

            case RouterUiAction.ClearPackets:
                router.Value.Comp.RecordedPackets.Clear();
                Dirty(router.Value);
                ent.Comp.StatusMessage = "Cleared router packet buffer.";
                break;
        }

        UpdateRouterProgramUi(ent.Owner);
    }

    private void OnSnifferUiMessage(Entity<PacketSnifferProgramComponent> ent, ref PacketSnifferActionMessage msg)
    {
        var compUid = ResolveComputerFromProgram(ent.Owner);
        switch (msg.Action)
        {
            case PacketSnifferAction.Refresh:
                ent.Comp.StatusMessage = "Packet capture refreshed.";
                break;

            case PacketSnifferAction.ClearLogs:
                if (FindNearestRouter(compUid) is { } clrRouter)
                {
                    clrRouter.Comp.RecordedPackets.Clear();
                    Dirty(clrRouter);
                }
                if (TryComp<NetworkBandwidthComponent>(compUid, out var localBw))
                {
                    localBw.StoredPackets.Clear();
                    Dirty(compUid, localBw);
                }
                ent.Comp.StatusMessage = "Packet capture logs cleared.";
                break;

            case PacketSnifferAction.SaveLogsToFile:
                if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
                {
                    var router = FindNearestRouter(compUid);
                    var packets = router?.Comp.RecordedPackets ?? new List<NetworkDataPacket>();
                    var lines = new List<string> { "=== PACKET CAPTURE DUMP ===" };
                    foreach (var p in packets)
                        lines.Add(GetPacketReadableSummary(p));

                    var dumpFile = new TextVirtualFile
                    {
                        Name = "capture",
                        Extension = "txt",
                        Directory = "/logs/",
                        TextContent = string.Join("\n", lines),
                        Size = Math.Max(64, lines.Count * 64)
                    };
                    _fileSystem.TryWriteFile(driveUid.Value, dumpFile, fs);
                    ent.Comp.StatusMessage = "Capture saved to /logs/capture.txt.";
                }
                else
                {
                    ent.Comp.StatusMessage = "Cannot save capture: no hard drive installed.";
                }
                break;

            case PacketSnifferAction.EmitPacket:
                if (!HasActiveNetworkCard(compUid))
                {
                    ent.Comp.StatusMessage = "Cannot emit packet: no active network card installed.";
                    break;
                }

                var srcGuid = string.IsNullOrWhiteSpace(msg.SourceGuid) ? EnsureDeviceGuid(compUid) : msg.SourceGuid.Trim().ToUpperInvariant();
                var tgtGuid = string.IsNullOrWhiteSpace(msg.TargetGuid) ? "BROADCAST" : msg.TargetGuid.Trim().ToUpperInvariant();
                var pkt = new NetworkDataPacket
                {
                    PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    SourceAddress = srcGuid,
                    TargetAddress = tgtGuid,
                    Protocol = string.IsNullOrWhiteSpace(msg.Protocol) ? "TCP/RAW" : msg.Protocol.Trim(),
                    PayloadSummary = msg.Payload,
                    RawPayload = msg.Payload,
                    ByteSize = Math.Max(64, msg.Payload.Length * 2),
                    Timestamp = _timing.CurTime
                };

                _bandwidth.RecordDataTransmitted(compUid, pkt.ByteSize, isUpload: true);
                _bandwidth.StorePacket(compUid, pkt);
                _bandwidth.LogPacketToRouters(pkt, compUid);
                ent.Comp.StatusMessage = $"Injected {pkt.Protocol} frame ({pkt.ByteSize} B) -> {tgtGuid}.";
                break;

            case PacketSnifferAction.ModifyAndTransmitPacket:
                HandleModifyAndTransmitPacket(compUid, ent.Comp, msg);
                break;
        }

        UpdatePacketSnifferUi(ent.Owner);
    }

    private void HandleModifyAndTransmitPacket(EntityUid compUid, PacketSnifferProgramComponent snifferComp, PacketSnifferActionMessage msg)
    {
        if (!HasActiveNetworkCard(compUid))
        {
            snifferComp.StatusMessage = "Cannot transmit modified packet: no active network card installed.";
            return;
        }

        var router = FindNearestRouter(compUid);
        NetworkDataPacket? captured = null;

        if (router != null)
        {
            captured = router.Value.Comp.RecordedPackets.LastOrDefault(p =>
                p.PacketId.Equals(msg.PacketId, StringComparison.OrdinalIgnoreCase));
        }

        if (captured == null && TryComp<NetworkBandwidthComponent>(compUid, out var localBw))
        {
            captured = localBw.StoredPackets.LastOrDefault(p =>
                p.PacketId.Equals(msg.PacketId, StringComparison.OrdinalIgnoreCase));
        }

        if (captured == null)
        {
            snifferComp.StatusMessage = $"Packet '{msg.PacketId}' no longer in capture buffer.";
            return;
        }

        if (!captured.IsPlayerRelevant || !captured.IsEditable)
        {
            snifferComp.StatusMessage = $"Packet '{captured.PacketId}' ({captured.Protocol}) is not player-editable.";
            return;
        }

        var modifiedEntries = msg.ModifiedEntries != null
            ? new Dictionary<string, string>(msg.ModifiedEntries)
            : new Dictionary<string, string>(captured.EditableEntries);

        var destGuid = msg.ApplyToSource
            ? (!string.IsNullOrWhiteSpace(msg.SourceGuid) ? msg.SourceGuid.Trim().ToUpperInvariant() : captured.SourceAddress)
            : (!string.IsNullOrWhiteSpace(msg.TargetGuid) ? msg.TargetGuid.Trim().ToUpperInvariant() : captured.TargetAddress);

        if (!TryFindDeviceByGuid(destGuid, out var destCompUid))
        {
            snifferComp.StatusMessage = $"Destination device [{destGuid}] not found on network.";
            return;
        }

        var rawSummary = string.Join("; ", modifiedEntries.Select(kv => $"{kv.Key}={kv.Value}"));
        var modPkt = captured.Clone();
        modPkt.PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        modPkt.SourceAddress = EnsureDeviceGuid(compUid);
        modPkt.TargetAddress = destGuid;
        modPkt.EditableEntries = modifiedEntries;
        modPkt.RawPayload = rawSummary;
        modPkt.PayloadSummary = $"[MODIFIED] {captured.Protocol} ({modifiedEntries.Count} entries) -> {destGuid}";
        modPkt.ByteSize = Math.Max(128, rawSummary.Length * 2 + 64);
        modPkt.Timestamp = _timing.CurTime;

        if (!RecordAndValidatePacket(compUid, destCompUid, modPkt))
        {
            snifferComp.StatusMessage = $"Blocked: router denied modified {modPkt.Protocol} transmission to [{destGuid}].";
            return;
        }

        var markTampered = msg.ApplyToSource;
        if (!markTampered &&
            _computerQuery.TryComp(destCompUid, out var destOxyd) &&
            destOxyd.HostMachine is { } destHost &&
            (HasComp<DataServerComponent>(destHost) || HasComp<NetworkRouterComponent>(destHost)))
        {
            markTampered = true;
        }

        if (!ApplyModifiedPacketToComputerVfs(destCompUid, modPkt, markTampered, out var applyErr))
        {
            snifferComp.StatusMessage = $"Transmitted modified {modPkt.Protocol}, but target VFS update failed: {applyErr}";
            return;
        }

        snifferComp.StatusMessage = markTampered
            ? $"Poisoned server/router local copy {modPkt.FilePath} on [{destGuid}] ({modifiedEntries.Count} entries)."
            : $"Transmitted modified {modPkt.Protocol} to [{destGuid}] ({modifiedEntries.Count} entries).";
        RefreshAllPacketSniffers();
    }

    private bool ApplyModifiedPacketToComputerVfs(
        EntityUid destCompUid,
        NetworkDataPacket pkt,
        bool markTampered,
        out string error)
    {
        error = string.Empty;
        if (!_fileSystem.TryFindFileSystem(destCompUid, out var driveUid, out var fs))
        {
            error = "no hard drive on target computer";
            return false;
        }

        var path = pkt.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "packet has no associated VFS file path";
            return false;
        }

        if (path.Equals(CrewManifestProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            var stationName = pkt.Parameters.TryGetValue("__station", out var st) && !string.IsNullOrWhiteSpace(st)
                ? st
                : "Unknown Station";

            if (_fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, path, out var existingDb, fs) &&
                existingDb.Data.TryGetValue("__station", out var existingSt) &&
                !string.IsNullOrWhiteSpace(existingSt))
            {
                stationName = existingSt;
            }

            data["__station"] = stationName;
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (charName, jobTitle) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(charName))
                    continue;

                var jobProto = string.Empty;
                var jobIcon = "JobIconUnknown";
                if (pkt.Parameters.TryGetValue($"meta:{charName}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1)
                        jobProto = mCols[0];
                    if (mCols.Length >= 2)
                        jobIcon = mCols[1];
                }

                data[$"row_{idx++}"] = $"{charName.Trim()}\t{jobTitle.Trim()}\t{jobProto}\t{jobIcon}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "crew_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = data,
                Size = Math.Max(128, data.Count * 64)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        if (path.Equals(WantedListProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (charName, rawVal) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(charName))
                    continue;

                var parts = rawVal.Split('|', 2);
                var statusStr = parts[0].Trim();
                var reason = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var jobTitle = "Crew";
                var initiator = "Security";
                if (pkt.Parameters.TryGetValue($"meta:{charName}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1 && !string.IsNullOrWhiteSpace(mCols[0]))
                        jobTitle = mCols[0];
                    if (mCols.Length >= 2)
                        initiator = mCols[1];
                }

                data[$"rec_{idx++}"] = $"{charName.Trim()}\t{jobTitle}\t{statusStr}\t{reason}\t{initiator}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "wanted_list",
                Extension = "db",
                Directory = "/records/",
                TableName = "wanted_list",
                Data = data,
                Size = Math.Max(128, data.Count * 64)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        if (path.Equals(NewsReaderProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var data = new Dictionary<string, string>();
            if (markTampered)
                data["__tampered"] = "1";

            var idx = 0;
            foreach (var (title, content) in pkt.EditableEntries)
            {
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var author = "Anonymous";
                var ticks = _timing.CurTime.Ticks.ToString();
                if (pkt.Parameters.TryGetValue($"meta:{title}", out var metaRaw))
                {
                    var mCols = metaRaw.Split('\t');
                    if (mCols.Length >= 1 && !string.IsNullOrWhiteSpace(mCols[0]))
                        author = mCols[0];
                    if (mCols.Length >= 2 && !string.IsNullOrWhiteSpace(mCols[1]))
                        ticks = mCols[1];
                }

                data[$"art_{idx++}"] = $"{title.Trim()}\t{author}\t{ticks}\t{content.Replace('\t', ' ')}";
            }

            var updated = new DatabaseVirtualFile
            {
                Name = "articles",
                Extension = "db",
                Directory = "/news/",
                TableName = "station_news",
                Data = data,
                Size = Math.Max(128, data.Count * 128)
            };

            if (!_fileSystem.TryWriteFile(driveUid.Value, updated, fs))
            {
                error = "target storage full or read-only";
                return false;
            }

            _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
            EntityManager.System<StationAppsSystem>().RefreshRecordProgramsFromLocalCache(destCompUid, path);
            return true;
        }

        // Generic .db or .txt file override
        var slashIdx = path.LastIndexOf('/');
        var dir = slashIdx >= 0 ? path[..(slashIdx + 1)] : "/";
        var fileWithExt = slashIdx >= 0 ? path[(slashIdx + 1)..] : path;
        var dotIdx = fileWithExt.LastIndexOf('.');
        var fName = dotIdx > 0 ? fileWithExt[..dotIdx] : fileWithExt;
        var fExt = dotIdx > 0 ? fileWithExt[(dotIdx + 1)..] : "db";

        VirtualFile genericFile;
        if (fExt.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            var body = pkt.EditableEntries.TryGetValue("content", out var textBody)
                ? textBody
                : string.Join("\n", pkt.EditableEntries.Select(kv => $"{kv.Key}: {kv.Value}"));
            genericFile = new TextVirtualFile
            {
                Name = fName,
                Extension = fExt,
                Directory = dir,
                TextContent = body,
                Size = Math.Max(64, body.Length)
            };
        }
        else
        {
            var dbData = new Dictionary<string, string>(pkt.EditableEntries);
            if (markTampered)
                dbData["__tampered"] = "1";
            genericFile = new DatabaseVirtualFile
            {
                Name = fName,
                Extension = fExt,
                Directory = dir,
                TableName = fName,
                Data = dbData,
                Size = Math.Max(128, dbData.Count * 64)
            };
        }

        if (!_fileSystem.TryWriteFile(driveUid.Value, genericFile, fs))
        {
            error = "target storage full or read-only";
            return false;
        }

        _fileSystem.LogSystemActivity(driveUid.Value, "NET-PKT", $"Updated {path} from {pkt.Protocol} ({pkt.PacketId})");
        return true;
    }

    public void RefreshDnsDirectory(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        EnsureDebugServerIfNeeded(router.Owner);
#endif
        var serverQuery = EntityQueryEnumerator<DataServerComponent>();
        while (serverQuery.MoveNext(out var sUid, out var sComp))
        {
            var targetComp = UnwrapComputerEntity(sUid);
            if (!HasActiveNetworkCard(targetComp))
                continue;

            var guid = EnsureDeviceGuid(targetComp);
            router.Comp.DnsRecords[MetaData(sUid).EntityName] = guid;
            if (_debugFallbackQuery.HasComp(sUid))
                router.Comp.DnsRecords["DEV_DEBUG_SERVER"] = guid;
            foreach (var proto in sComp.ServerProtocols)
                router.Comp.DnsRecords[proto] = guid;
        }
        Dirty(router);
    }

    public EntityUid UnwrapComputerEntity(EntityUid uid)
    {
        if (_hostQuery.TryComp(uid, out var host) && host.ComputerEntity is { } cUid && Exists(cUid))
            return cUid;
        return uid;
    }

    public EntityUid ResolveComputerFromProgram(EntityUid uid)
    {
        if (_programQuery.TryComp(uid, out var prog) && prog.Computer is { } cUid && Exists(cUid))
            return cUid;
        return UnwrapComputerEntity(uid);
    }

    public bool HasActiveNetworkCard(EntityUid uid)
    {
        var compUid = UnwrapComputerEntity(uid);
        if (_computerQuery.TryComp(compUid, out var comp))
        {
            if (comp.NetworkWireCut || (comp.RequirePowerToBoot && !comp.IsPowered))
                return false;

            return _statsQuery.TryComp(compUid, out var stats) && stats.HasNetworkCard && stats.NetworkBandwidthBps > 0;
        }

        if (TryComp<NetworkBandwidthComponent>(compUid, out var bw))
            return bw.MaxBps > 0;

        return false;
    }

    public string EnsureDeviceGuid(EntityUid uid)
    {
        var target = UnwrapComputerEntity(uid);
        return _guid.GetGuid(target);
    }

    public bool TryFindDeviceByGuid(string guid, out EntityUid foundUid)
    {
        foundUid = EntityUid.Invalid;
        if (string.IsNullOrWhiteSpace(guid))
            return false;

        var clean = guid.Trim().ToUpperInvariant();
        if (_guid.TryGetEntityByGuid(clean, out var res) && res.HasValue)
        {
            foundUid = UnwrapComputerEntity(res.Value);
            return true;
        }

        var compQuery = EntityQueryEnumerator<OxydComputerComponent, DeviceGuidComponent>();
        while (compQuery.MoveNext(out var uid, out _, out var gComp))
        {
            if (gComp.Guid.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                foundUid = uid;
                return true;
            }
        }

        return false;
    }

    public Entity<NetworkRouterComponent>? FindNearestRouter(EntityUid origin)
    {
        var originCoords = _transform.GetMapCoordinates(origin);
        Entity<NetworkRouterComponent>? best = null;
        var bestDist = float.MaxValue;

        if (originCoords.MapId != MapId.Nullspace)
        {
            var candidates = _lookup.GetEntitiesInRange<NetworkRouterComponent>(
                originCoords,
                RouterLookupRange,
                LookupFlags.All | LookupFlags.Approximate);

            foreach (var rEnt in candidates)
            {
                if (_debugFallbackQuery.HasComp(rEnt.Owner))
                    continue;

                if (!IsRouterOperational(rEnt.Owner))
                    continue;

                var dist = (_transform.GetMapCoordinates(rEnt.Owner).Position - originCoords.Position).LengthSquared();
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = rEnt;
                }
            }
        }

#if DEBUG
        if (best == null &&
            !_flushingEntities &&
            !_ensuringDebugRouter &&
            LifeStage(origin) == EntityLifeStage.MapInitialized &&
            !TerminatingOrDeleted(origin) &&
            !HasOperationalRealCentralServer() &&
            TryEnsureDebugGlobalRouter(out var debugRouter))
        {
            return debugRouter;
        }
#endif

        return best;
    }

    private Entity<NetworkRouterComponent>? FindActiveRouter(EntityUid origin)
    {
        if (TryComp<OxydComputerComponent>(origin, out var oxyd) && oxyd.HostMachine is { } hm && TryComp<NetworkRouterComponent>(hm, out var hmRouter))
            return (hm, hmRouter);

        return FindNearestRouter(origin);
    }

    public bool IsRouterOperational(EntityUid routerHostOrComp)
    {
        if (TryComp<Content.Server.Power.Components.ApcPowerReceiverComponent>(routerHostOrComp, out var apc) && !apc.Powered)
            return false;

        var routerComp = UnwrapComputerEntity(routerHostOrComp);
        if (_computerQuery.TryComp(routerComp, out var comp))
        {
            if (comp.NetworkWireCut || comp.PowerWireCut)
                return false;

            if (_statsQuery.TryComp(routerComp, out var stats) && !stats.HasNetworkCard)
                return false;
        }

        if (TryComp<NetworkBandwidthComponent>(routerComp, out var bw) && bw.IsSaturated)
            return false;

        return true;
    }

    public bool IsNetworkConnected(EntityUid uid)
    {
        var compUid = UnwrapComputerEntity(uid);
        if (!HasActiveNetworkCard(compUid))
            return false;

        var router = FindNearestRouter(compUid);
        if (router == null)
            return false;

        var guid = EnsureDeviceGuid(compUid);
        if (router.Value.Comp.BlacklistedGuids.Contains(guid))
            return false;

        RegisterDeviceOnRouter(router.Value, guid);
        return true;
    }

    public bool IsGuidBlacklisted(EntityUid contextUid, string guid)
    {
        var router = FindNearestRouter(contextUid);
        return router != null && router.Value.Comp.BlacklistedGuids.Contains(guid.Trim().ToUpperInvariant());
    }

    public string? ResolveDns(EntityUid origin, string serviceName)
    {
        if (!HasActiveNetworkCard(origin))
            return null;

        var router = FindNearestRouter(origin);
        if (router == null)
            return null;

        RefreshDnsDirectory(router.Value);
        if (router.Value.Comp.DnsRecords.TryGetValue(serviceName, out var exact))
            return exact;

        foreach (var (k, v) in router.Value.Comp.DnsRecords)
        {
            if (k.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    public void RegisterDeviceOnRouter(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(clean) && !router.Comp.ConnectedDevices.Contains(clean))
        {
            router.Comp.ConnectedDevices.Add(clean);
            Dirty(router);
        }
    }

    public void RefreshRouterDevices(Entity<NetworkRouterComponent> router)
    {
#if DEBUG
        EnsureDebugServerIfNeeded(router.Owner);
#endif

        router.Comp.ConnectedDevices.Clear();
        var compQuery = EntityQueryEnumerator<OxydComputerComponent>();
        while (compQuery.MoveNext(out var uid, out _))
        {
            if (_debugFallbackQuery.TryComp(uid, out var marker) && !marker.IsServer)
                continue;

            if (!HasActiveNetworkCard(uid))
                continue;

            var guid = EnsureDeviceGuid(uid);
            if (!router.Comp.ConnectedDevices.Contains(guid))
                router.Comp.ConnectedDevices.Add(guid);
        }

        var srvQuery = EntityQueryEnumerator<DataServerComponent>();
        while (srvQuery.MoveNext(out var sUid, out _))
        {
            var sComp = UnwrapComputerEntity(sUid);
            if (!HasActiveNetworkCard(sComp))
                continue;

            var guid = EnsureDeviceGuid(sComp);
            if (!router.Comp.ConnectedDevices.Contains(guid))
                router.Comp.ConnectedDevices.Add(guid);
        }

        Dirty(router);
    }

    public bool RecordAndValidatePacket(EntityUid sender, EntityUid target, NetworkDataPacket packet)
    {
        var sComp = UnwrapComputerEntity(sender);
        var tComp = UnwrapComputerEntity(target);

        if (!HasActiveNetworkCard(sComp) || !HasActiveNetworkCard(tComp))
            return false;

        var router = _debugFallbackQuery.HasComp(sComp)
            ? FindNearestRouter(tComp) ?? FindNearestRouter(sComp)
            : FindNearestRouter(sComp) ?? FindNearestRouter(tComp);
        if (router == null)
            return false;

        var sGuid = EnsureDeviceGuid(sComp);
        var tGuid = EnsureDeviceGuid(tComp);
        if (router.Value.Comp.BlacklistedGuids.Contains(sGuid) || router.Value.Comp.BlacklistedGuids.Contains(tGuid))
            return false;

        var transferBps = _bandwidth.GetEffectiveTransferRateBps(sComp, tComp);
        if (transferBps <= 0)
            return false;

        RegisterDeviceOnRouter(router.Value, sGuid);
        RegisterDeviceOnRouter(router.Value, tGuid);

        _bandwidth.RecordDataTransmitted(sComp, packet.ByteSize, isUpload: true);
        _bandwidth.RecordDataTransmitted(tComp, packet.ByteSize, isUpload: false);
        _bandwidth.StorePacket(sComp, packet);
        _bandwidth.StorePacket(tComp, packet);
        _bandwidth.LogPacketToRouters(packet, sComp);

        RefreshAllPacketSniffers();
        return true;
    }

    public string RegisterActiveConnection(EntityUid origin, string sourceGuid, string targetGuid, string connectionType, string description)
    {
        var router = FindNearestRouter(origin);
        if (router == null)
            return string.Empty;

        var connId = $"{connectionType}-{sourceGuid}-{targetGuid}";
        router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connId);
        router.Value.Comp.ActiveConnections.Add(new ActiveNetworkConnectionRecord
        {
            ConnectionId = connId,
            SourceGuid = sourceGuid,
            TargetGuid = targetGuid,
            ConnectionType = connectionType,
            Description = description
        });
        Dirty(router.Value);
        return connId;
    }

    public void UnregisterActiveConnection(EntityUid origin, string connectionId)
    {
        var router = FindNearestRouter(origin);
        if (router == null)
            return;

        if (router.Value.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0)
            Dirty(router.Value);
    }

    public void CloseConnectionById(Entity<NetworkRouterComponent> router, string connectionId)
    {
        var removed = router.Comp.ActiveConnections.Where(c => c.ConnectionId == connectionId).ToList();
        if (router.Comp.ActiveConnections.RemoveAll(c => c.ConnectionId == connectionId) > 0)
            Dirty(router);

        foreach (var rec in removed)
            TerminateSessionsForConnection(rec.SourceGuid, rec.TargetGuid);
    }

    public void BlacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (router.Comp.BlacklistedGuids.Add(clean))
            Dirty(router);

        TerminateAllSessionsForGuid(clean);
    }

    public void UnblacklistGuid(Entity<NetworkRouterComponent> router, string guid)
    {
        var clean = guid.Trim().ToUpperInvariant();
        if (router.Comp.BlacklistedGuids.Remove(clean))
            Dirty(router);
    }

    public void TerminateAllSessionsForGuid(string guid)
    {
        var screenSys = EntityManager.System<ServerRemoteScreenSystem>();
        if (TryFindDeviceByGuid(guid, out var directTarget))
            screenSys.DetachViewer(EntityUid.Invalid, targetComputerUid: directTarget);

        var termQuery = EntityQueryEnumerator<TerminalProgramComponent, ProgramComponent>();
        while (termQuery.MoveNext(out var progUid, out var term, out var prog))
        {
            var localGuid = prog.Computer is { } c ? EnsureDeviceGuid(c) : string.Empty;
            if (term.ConnectedRemoteGuid == guid || localGuid == guid)
            {
                var prevRemote = term.ConnectedRemoteGuid;
                EntityUid? prevTargetUid = prevRemote != null && TryFindDeviceByGuid(prevRemote, out var rt) ? rt : null;

                term.ConnectedRemoteGuid = null;
                term.IsScreenAttached = false;
                term.History.Add($"[NETWORK] Session terminated by router firewall ({guid} blacklisted).");

                if (prevTargetUid != null)
                    screenSys.DetachViewer(progUid, targetComputerUid: prevTargetUid);

                if (localGuid == guid)
                {
                    DetachComputerViewers(screenSys, progUid, prog.Computer, prevTargetUid);
                }

                if (_ui.HasUi(progUid, ProgramUiKey.Key))
                    EntityManager.System<TerminalSystem>().UpdateTerminalUi(progUid);
            }
        }
    }

    private void TerminateSessionsForConnection(string srcGuid, string dstGuid)
    {
        var screenSys = EntityManager.System<ServerRemoteScreenSystem>();
        var termQuery = EntityQueryEnumerator<TerminalProgramComponent, ProgramComponent>();
        while (termQuery.MoveNext(out var progUid, out var term, out var prog))
        {
            var localGuid = prog.Computer is { } c ? EnsureDeviceGuid(c) : string.Empty;
            if ((localGuid == srcGuid && term.ConnectedRemoteGuid == dstGuid) ||
                (localGuid == dstGuid && term.ConnectedRemoteGuid == srcGuid))
            {
                var prevRemote = term.ConnectedRemoteGuid;
                EntityUid? prevTargetUid = prevRemote != null && TryFindDeviceByGuid(prevRemote, out var rt) ? rt : null;

                term.ConnectedRemoteGuid = null;
                term.IsScreenAttached = false;
                term.History.Add("[NETWORK] Connection forcefully dropped by station router.");

                DetachComputerViewers(screenSys, progUid, prog.Computer, prevTargetUid);

                if (_ui.HasUi(progUid, ProgramUiKey.Key))
                    EntityManager.System<TerminalSystem>().UpdateTerminalUi(progUid);
            }
        }
    }

    private void DetachComputerViewers(ServerRemoteScreenSystem screenSys, EntityUid progUid, EntityUid? compUid, EntityUid? targetCompUid)
    {
        var actors = new HashSet<EntityUid>();
        if (TryComp<UserInterfaceComponent>(progUid, out var pUi))
        {
            foreach (var actorSet in pUi.Actors.Values)
            {
                foreach (var act in actorSet)
                    actors.Add(act);
            }
        }

        if (compUid is { } cUid && TryComp<UserInterfaceComponent>(cUid, out var cUi))
        {
            foreach (var actorSet in cUi.Actors.Values)
            {
                foreach (var act in actorSet)
                    actors.Add(act);
            }
        }

        foreach (var act in actors)
            screenSys.DetachViewer(progUid, act, targetCompUid);
    }

    public void RefreshAllRouterProgramUis()
    {
        var q = EntityQueryEnumerator<RouterProgramComponent>();
        while (q.MoveNext(out var uid, out _))
            UpdateRouterProgramUi(uid);
    }

    public void RefreshAllPacketSniffers()
    {
        var q = EntityQueryEnumerator<PacketSnifferProgramComponent>();
        while (q.MoveNext(out var uid, out _))
            UpdatePacketSnifferUi(uid);
    }

    public void UpdateRouterProgramUi(EntityUid progUid)
    {
        if (!TryComp<RouterProgramComponent>(progUid, out var rProg))
            return;

        var compUid = ResolveComputerFromProgram(progUid);
        var router = FindActiveRouter(compUid);
        var bw = router != null ? CompOrNull<NetworkBandwidthComponent>(UnwrapComputerEntity(router.Value.Owner)) : null;

        var devices = new List<RouterConnectedDeviceDto>();
        if (router != null)
        {
            RefreshRouterDevices(router.Value);
            foreach (var guid in router.Value.Comp.ConnectedDevices)
            {
                if (!TryFindDeviceByGuid(guid, out var dUid))
                    continue;

                var dStats = CompOrNull<ComputerStatsComponent>(dUid);
                devices.Add(new RouterConnectedDeviceDto(
                    MetaData(dUid).EntityName,
                    guid,
                    dStats?.NetworkBandwidthBps ?? 0,
                    dStats?.NetworkUploadBandwidthBps ?? 0,
                    HasActiveNetworkCard(dUid),
                    router.Value.Comp.BlacklistedGuids.Contains(guid)));
            }
        }

        var state = new RouterUiState(
            router != null ? EnsureDeviceGuid(router.Value.Owner) : "NO-ROUTER",
            router != null && IsRouterOperational(router.Value.Owner),
            bw?.MaxBps ?? 0,
            bw?.MaxUploadBps ?? 0,
            devices,
            router?.Comp.ActiveConnections ?? new(),
            router?.Comp.RecordedPackets ?? new(),
            router?.Comp.BlacklistedGuids.ToList() ?? new(),
            router?.Comp.DnsRecords ?? new(),
            rProg.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    public void UpdatePacketSnifferUi(EntityUid progUid)
    {
        if (!TryComp<PacketSnifferProgramComponent>(progUid, out var sProg))
            return;

        var compUid = ResolveComputerFromProgram(progUid);
        var hasNic = HasActiveNetworkCard(compUid);
        var router = hasNic ? FindNearestRouter(compUid) : null;
        var packets = new List<NetworkDataPacket>();

        if (hasNic)
        {
            if (router != null)
                packets.AddRange(router.Value.Comp.RecordedPackets);
            else if (TryComp<NetworkBandwidthComponent>(compUid, out var bw))
                packets.AddRange(bw.StoredPackets);
        }

        var statusMsg = router != null && IsUsingDebugFallbackRouter(router.Value)
            ? $"{sProg.StatusMessage} [Global Net: {MetaData(router.Value.Owner).EntityName}]"
            : sProg.StatusMessage;

        var state = new PacketSnifferUiState(
            router != null ? EnsureDeviceGuid(router.Value.Owner) : EnsureDeviceGuid(compUid),
            hasNic,
            packets,
            statusMsg);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    public static string GetPacketReadableSummary(NetworkDataPacket pkt)
    {
        var timeStr = pkt.Timestamp.ToString(@"mm\:ss");
        var flag = pkt.Dropped ? " [DROPPED]" : pkt.IsEncrypted ? " [ENC]" : "";
        return $"[{timeStr}] {pkt.SourceAddress} -> {pkt.TargetAddress} [{pkt.Protocol}] {pkt.ByteSize}B{flag}: {pkt.PayloadSummary}";
    }
}
