using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.CrewManifest;
using Content.Server.MassMedia.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CriminalRecords;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Servers;

/// <summary>
/// Physical mainframe server hosting station repository files, blueprints, executables (.nte),
/// and databases on its physical hard drive entity.
/// </summary>
public sealed partial class DataServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNet = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private NewsSystem _news = default!;

#if DEBUG
    private static readonly EntProtoId DebugDataServerPrototype = "BaseDataServer";
    public const string DebugServerName = "DEV DEBUG SERVER";

    private EntityUid _debugServerHost;
    private EntityUid _debugServerComp;
    private bool _flushingEntities;
#endif

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DataServerComponent, DeviceNetworkPacketEvent<ServiceRequestPayload>>(OnServiceRequest);
#if DEBUG
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        EntityManager.BeforeEntityFlush += OnBeforeEntityFlush;
        EntityManager.AfterEntityFlush += OnAfterEntityFlush;
#endif
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
        _debugServerHost = EntityUid.Invalid;
        _debugServerComp = EntityUid.Invalid;
    }

    private void OnAfterEntityFlush()
    {
        _flushingEntities = false;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _debugServerHost = EntityUid.Invalid;
        _debugServerComp = EntityUid.Invalid;
    }
#endif

    public bool IsDebugFallbackServer(EntityUid uid)
    {
#if DEBUG
        return uid != EntityUid.Invalid &&
            (uid == _debugServerHost || uid == _debugServerComp || HasComp<DebugFallbackNetworkEntityComponent>(uid));
#else
        return false;
#endif
    }

#if DEBUG
    /// <summary>
    /// Ensures the #if DEBUG fallback central server ("DEV DEBUG SERVER") is spawned, booted, and populated.
    /// </summary>
    public void EnsureDebugRepositoryServer()
    {
        TryEnsureDebugRepositoryServer(out _, out _, out _, out _);
    }
#endif

    private void OnServiceRequest(Entity<DataServerComponent> ent, ref DeviceNetworkPacketEvent<ServiceRequestPayload> args)
    {
        if (!_router.IsRouterOperational(ent.Owner) || !_router.HasActiveNetworkCard(ent.Owner))
            return;

        if (!ent.Comp.AcceptsPublicReads && args.Data.AuthToken != ent.Comp.NetworkMasterKey)
            return;

        if (!ent.Comp.ServerProtocols.Contains(args.Data.ServiceName))
            return;

        var driveUid = GetServerDrive(ent.Owner, out var fs);
        if (driveUid == null || fs == null)
            return;

        if (args.Data.ServiceName == "ManifestService")
        {
            var station = _station.GetOwningStation(ent.Owner) ?? _station.GetStationsSet().FirstOrDefault();
            if (!_fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, CrewManifestProgramComponent.CacheFilePath, out var manifestFile, fs) &&
                !_fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, "/records/manifest.db", out manifestFile, fs) &&
                station != EntityUid.Invalid)
            {
                var snap = BuildLiveRecordSnapshot(CrewManifestProgramComponent.CacheFilePath, station, out var liveHash, out var tableName, out var dir, out var name);
                if (snap != null)
                {
                    snap["__source_hash"] = liveHash;
                    manifestFile = new DatabaseVirtualFile
                    {
                        Name = name,
                        Extension = "db",
                        Directory = dir,
                        TableName = tableName,
                        Data = snap,
                        Size = Math.Max(128, snap.Count * 64)
                    };
                    _fileSystem.TryWriteFile(driveUid.Value, manifestFile, fs);
                }
            }

            if (manifestFile != null)
            {
                _bandwidth.RecordDataTransmitted(ent.Owner, manifestFile.Size, isUpload: true);
                var response = new FileTransferPayload
                {
                    Path = manifestFile.Directory,
                    FileName = manifestFile.Name,
                    Extension = manifestFile.Extension,
                    FileContent = string.Join("\n", manifestFile.Data.Select(kv => $"{kv.Key}={kv.Value}")),
                    AuthToken = args.Data.AuthToken
                };
                _deviceNet.SendPacket(ent.Owner, args.SenderAddress, ref response, args.Frequency);
            }
        }
    }

    /// <summary>
    /// Resolves the online computer-backed network handler (protocol-matching DataServer computer,
    /// general DataServer computer, operational StationNetworkRouter computer, or #if DEBUG server computer)
    /// that serves local VFS copies of station databases.
    /// </summary>
    public bool TryFindRecordHostComputer(
        EntityUid originCompUid,
        string serviceProtocol,
        string filePath,
        out EntityUid hostUid,
        out EntityUid hostCompUid,
        [NotNullWhen(true)] out EntityUid? hostDriveUid,
        [NotNullWhen(true)] out FileSystemComponent? hostFs,
        out string error)
    {
        hostUid = EntityUid.Invalid;
        hostCompUid = EntityUid.Invalid;
        hostDriveUid = null;
        hostFs = null;
        error = string.Empty;

        if (!_router.HasActiveNetworkCard(originCompUid))
        {
            error = "No active network card installed.";
            return false;
        }

        var originGuid = _router.EnsureDeviceGuid(originCompUid);
        if (_router.IsGuidBlacklisted(originCompUid, originGuid))
        {
            error = "Station network router unreachable or blacklisted.";
            return false;
        }

        var router = _router.FindNearestRouter(originCompUid);

        // If the active router computer itself has a tampered local override for this file, it serves its own copy!
        if (router != null &&
            !_router.IsUsingDebugFallbackRouter(router.Value) &&
            _fileSystem.TryFindFileSystem(router.Value.Owner, out var rDrive, out var rFs) &&
            rFs.Files.TryGetValue(filePath, out var rRaw) &&
            rRaw is DatabaseVirtualFile rDb &&
            rDb.Data.TryGetValue("__tampered", out var rTamp) &&
            rTamp == "1")
        {
            hostUid = router.Value.Owner;
            hostCompUid = _router.UnwrapComputerEntity(router.Value.Owner);
            hostDriveUid = rDrive;
            hostFs = rFs;
            return true;
        }

        if (_router.IsNetworkConnected(originCompUid))
        {
            // 1. Prefer an online real DataServer computer explicitly serving this protocol.
            EntityUid fallbackSrvUid = EntityUid.Invalid;
            EntityUid fallbackSrvComp = EntityUid.Invalid;
            EntityUid? fallbackDrive = null;
            FileSystemComponent? fallbackFs = null;

            var query = EntityQueryEnumerator<DataServerComponent>();
            while (query.MoveNext(out var uid, out var srvComp))
            {
                if (IsDebugFallbackServer(uid))
                    continue;

                var cUid = _router.UnwrapComputerEntity(uid);
                if (!_router.IsRouterOperational(uid) || !_router.HasActiveNetworkCard(cUid))
                    continue;

                if (!_fileSystem.TryFindFileSystem(uid, out var dUid, out var fsComp))
                    continue;

                if (!string.IsNullOrEmpty(serviceProtocol) && srvComp.ServerProtocols.Contains(serviceProtocol))
                {
                    hostUid = uid;
                    hostCompUid = cUid;
                    hostDriveUid = dUid;
                    hostFs = fsComp;
                    return true;
                }

                if (fallbackDrive == null)
                {
                    fallbackSrvUid = uid;
                    fallbackSrvComp = cUid;
                    fallbackDrive = dUid;
                    fallbackFs = fsComp;
                }
            }

            // 2. Any online real DataServer computer with an HDD.
            if (fallbackDrive != null && fallbackFs != null)
            {
                hostUid = fallbackSrvUid;
                hostCompUid = fallbackSrvComp;
                hostDriveUid = fallbackDrive;
                hostFs = fallbackFs;
                return true;
            }

            // 3. Operational real StationNetworkRouter computer acting as local network file handler!
            if (router != null &&
                !_router.IsUsingDebugFallbackRouter(router.Value) &&
                _fileSystem.TryFindFileSystem(router.Value.Owner, out var routerDrive, out var routerFs))
            {
                hostUid = router.Value.Owner;
                hostCompUid = _router.UnwrapComputerEntity(router.Value.Owner);
                hostDriveUid = routerDrive;
                hostFs = routerFs;
                return true;
            }
        }

#if DEBUG
        if (TryEnsureDebugRepositoryServer(out hostUid, out hostCompUid, out hostDriveUid, out hostFs))
            return true;
#else
        if (!_router.IsNetworkConnected(originCompUid))
        {
            error = "Station network router unreachable or blacklisted.";
            return false;
        }
#endif

        error = "No online Data Server or Router computer reachable on station network.";
        return false;
    }

    /// <summary>
    /// Retrieves a clone of the local record database stored on the network server/router computer's own HDD.
    /// Seeds the server/router computer's local copy from live station records on first access (or when live
    /// station records change and the host's local copy has not been manually edited/tampered).
    /// Always serves from the host computer's local VFS copy.
    /// </summary>
    public bool TryGetLocalRecordCopyFromHost(
        EntityUid originCompUid,
        string serviceProtocol,
        string filePath,
        EntityUid station,
        out EntityUid hostCompUid,
        [NotNullWhen(true)] out DatabaseVirtualFile? localCopy,
        out string error)
    {
        hostCompUid = EntityUid.Invalid;
        localCopy = null;

        if (!TryFindRecordHostComputer(originCompUid, serviceProtocol, filePath, out _, out hostCompUid, out var hostDriveUid, out var hostFs, out error))
            return false;

        var liveSnapshot = BuildLiveRecordSnapshot(filePath, station, out var liveHash, out var tableName, out var dir, out var name);

        if (hostFs.Files.TryGetValue(filePath, out var existingRaw) && existingRaw is DatabaseVirtualFile existingDb)
        {
            var isTampered = existingDb.Data.TryGetValue("__tampered", out var tVal) && tVal == "1";
            var hasSourceHash = existingDb.Data.TryGetValue("__source_hash", out var prevHash);

            // Preserve the server/router computer's local copy if it was tampered, manually created (no source hash),
            // or if live station state has not changed since it was last seeded.
            if (isTampered || !hasSourceHash || prevHash == liveHash || liveSnapshot == null)
            {
                localCopy = (DatabaseVirtualFile) existingDb.Clone();
                return true;
            }

            // Station records updated naturally on an untampered seeded file -> refresh the host computer's local copy.
            var refreshed = new DatabaseVirtualFile
            {
                Name = name,
                Extension = "db",
                Directory = dir,
                TableName = tableName,
                Data = liveSnapshot,
                Size = Math.Max(128, liveSnapshot.Count * 64)
            };
            refreshed.Data["__source_hash"] = liveHash;
            _fileSystem.TryWriteFile(hostDriveUid.Value, refreshed, hostFs);
            localCopy = (DatabaseVirtualFile) refreshed.Clone();
            return true;
        }

        // First access on this server/router computer: materialize its local VFS copy on its HDD!
        var seedData = liveSnapshot ?? new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(liveHash))
            seedData["__source_hash"] = liveHash;

        var created = new DatabaseVirtualFile
        {
            Name = name,
            Extension = "db",
            Directory = dir,
            TableName = tableName,
            Data = seedData,
            Size = Math.Max(128, seedData.Count * 64)
        };
        _fileSystem.TryWriteFile(hostDriveUid.Value, created, hostFs);
        localCopy = (DatabaseVirtualFile) created.Clone();
        return true;
    }

    private Dictionary<string, string>? BuildLiveRecordSnapshot(
        string filePath,
        EntityUid station,
        out string sourceHash,
        out string tableName,
        out string dir,
        out string name)
    {
        sourceHash = string.Empty;
        tableName = "records";
        dir = "/records/";
        name = "records";

        if (filePath.Equals(CrewManifestProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            tableName = "crew_manifest";
            dir = "/records/";
            name = "crew_manifest";

            if (station == EntityUid.Invalid)
                return null;

            var (stName, liveEntries) = _crewManifest.GetCrewManifest(station);
            var data = new Dictionary<string, string> { ["__station"] = stName };
            var sb = new StringBuilder(stName);
            if (liveEntries?.Entries != null)
            {
                var idx = 0;
                foreach (var entry in liveEntries.Entries)
                {
                    var row = $"{entry.Name}\t{entry.JobTitle}\t{entry.JobPrototype}\t{entry.JobIcon}";
                    data[$"row_{idx++}"] = row;
                    sb.Append('\n').Append(row);
                }
            }

            sourceHash = sb.ToString().GetHashCode().ToString("X8");
            return data;
        }

        if (filePath.Equals(WantedListProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            tableName = "wanted_list";
            dir = "/records/";
            name = "wanted_list";

            if (station == EntityUid.Invalid)
                return null;

            var data = new Dictionary<string, string>();
            var sb = new StringBuilder();
            var records = _records.GetRecordsOfType<CriminalRecord>(station)
                .Where(cr => cr.Item2.Status is not SecurityStatus.None || cr.Item2.History.Count > 0)
                .ToList();

            var idx = 0;
            foreach (var (recId, rec) in records)
            {
                var key = new StationRecordKey(recId, station);
                if (_records.TryGetRecord(key, out GeneralStationRecord? gen))
                {
                    var row = $"{gen.Name}\t{gen.JobTitle}\t{rec.Status}\t{rec.Reason ?? ""}\t{rec.InitiatorName ?? ""}";
                    data[$"rec_{idx++}"] = row;
                    sb.Append('\n').Append(row);
                }
            }

            sourceHash = sb.ToString().GetHashCode().ToString("X8");
            return data;
        }

        if (filePath.Equals(NewsReaderProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            tableName = "station_news";
            dir = "/news/";
            name = "articles";

            if (station == EntityUid.Invalid || !_news.TryGetNewsArticles(station, out var liveArticles))
                return null;

            var data = new Dictionary<string, string>();
            var sb = new StringBuilder();
            for (var i = 0; i < liveArticles.Count; i++)
            {
                var a = liveArticles[i];
                var row = $"{a.Title}\t{a.Author ?? "Anonymous"}\t{a.ShareTime.Ticks}\t{a.Content.Replace('\t', ' ')}";
                data[$"art_{i}"] = row;
                sb.Append('\n').Append(row);
            }

            sourceHash = sb.ToString().GetHashCode().ToString("X8");
            return data;
        }

        if (filePath.Equals(StationRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            tableName = "station_records";
            dir = "/records/";
            name = "station_records";

            if (station == EntityUid.Invalid)
                return null;

            var stName = Exists(station) ? MetaData(station).EntityName : "Unknown Station";
            var data = new Dictionary<string, string> { ["__station"] = stName };
            var sb = new StringBuilder(stName);
            var records = _records.GetRecordsOfType<GeneralStationRecord>(station).OrderBy(r => r.Item1).ToList();
            foreach (var (recId, gen) in records)
            {
                var row = RecordsDbCodec.EncodeStationRecordRow(
                    gen.Name,
                    gen.JobTitle,
                    gen.JobPrototype,
                    gen.JobIcon,
                    gen.Age,
                    gen.Species,
                    gen.Gender.ToString(),
                    gen.Fingerprint ?? string.Empty,
                    gen.DNA ?? string.Empty);
                data[$"rec_{recId}"] = row;
                sb.Append('\n').Append(recId).Append(':').Append(row);
            }

            sourceHash = sb.ToString().GetHashCode().ToString("X8");
            return data;
        }

        if (filePath.Equals(CriminalRecordsProgramComponent.CacheFilePath, StringComparison.OrdinalIgnoreCase))
        {
            tableName = "criminal_records";
            dir = "/records/";
            name = "criminal_records";

            if (station == EntityUid.Invalid)
                return null;

            var data = new Dictionary<string, string>();
            var sb = new StringBuilder();
            var generalRecords = _records.GetRecordsOfType<GeneralStationRecord>(station).OrderBy(r => r.Item1).ToList();
            foreach (var (recId, gen) in generalRecords)
            {
                var key = new StationRecordKey(recId, station);
                var status = SecurityStatus.None;
                var reason = string.Empty;
                var initiator = string.Empty;
                var histList = new List<CrimeHistoryEntryDto>();

                if (_records.TryGetRecord(key, out CriminalRecord? crim))
                {
                    status = crim.Status;
                    reason = crim.Reason ?? string.Empty;
                    initiator = crim.InitiatorName ?? string.Empty;
                    for (var i = 0; i < crim.History.Count; i++)
                    {
                        var h = crim.History[i];
                        var ts = TimeSpan.FromSeconds(Math.Truncate(h.AddTime.TotalSeconds)).ToString(@"hh\:mm\:ss");
                        histList.Add(new CrimeHistoryEntryDto(i, ts, h.Crime, h.InitiatorName ?? string.Empty));
                    }
                }

                var row = RecordsDbCodec.EncodeCriminalRecordRow(gen.Name, gen.JobTitle, status, reason, initiator, histList);
                data[$"crim_{recId}"] = row;
                sb.Append('\n').Append(recId).Append(':').Append(row);
            }

            sourceHash = sb.ToString().GetHashCode().ToString("X8");
            return data;
        }

        return null;
    }

    /// <summary>
    /// Writes an updated authoritative record database back to the serving DataServer / Router computer's HDD.
    /// Marks <c>__authoritative = "1"</c> (or removes stale <c>__source_hash</c>) so manual/app edits persist
    /// and are not clobbered by background station snapshots.
    /// </summary>
    public bool TryWriteRecordCopyToHost(
        EntityUid originCompUid,
        string serviceProtocol,
        string filePath,
        DatabaseVirtualFile updatedFile,
        out EntityUid hostCompUid,
        out string error)
    {
        hostCompUid = EntityUid.Invalid;
        if (!TryFindRecordHostComputer(originCompUid, serviceProtocol, filePath, out _, out hostCompUid, out var hostDriveUid, out var hostFs, out error))
            return false;

        var toWrite = (DatabaseVirtualFile) updatedFile.Clone();
        toWrite.Data.Remove("__source_hash");
        if (!_fileSystem.TryWriteFile(hostDriveUid.Value, toWrite, hostFs))
        {
            error = "Host data server storage full or read-only.";
            return false;
        }

        _fileSystem.LogSystemActivity(hostDriveUid.Value, "DB-WRITE", $"Updated {filePath} ({toWrite.Size} B)");
        return true;
    }

    /// <summary>
    /// Locates the best operational DataServer HDD for the Network Core OS to write records to,
    /// falling back to the Network Core Router's own HDD if no dedicated DataServer is online.
    /// </summary>
    public bool TryGetCoreRecordDrive(
        EntityUid coreRouterHostOrComp,
        string serviceProtocol,
        out EntityUid hostCompUid,
        [NotNullWhen(true)] out EntityUid? hostDriveUid,
        [NotNullWhen(true)] out FileSystemComponent? hostFs)
    {
        hostCompUid = EntityUid.Invalid;
        hostDriveUid = null;
        hostFs = null;

        EntityUid fallbackComp = EntityUid.Invalid;
        EntityUid? fallbackDrive = null;
        FileSystemComponent? fallbackFs = null;

        var query = EntityQueryEnumerator<DataServerComponent>();
        while (query.MoveNext(out var uid, out var srvComp))
        {
            if (IsDebugFallbackServer(uid))
                continue;

            var cUid = _router.UnwrapComputerEntity(uid);
            if (!_router.IsRouterOperational(uid) || !_router.HasActiveNetworkCard(cUid))
                continue;

            if (!_fileSystem.TryFindFileSystem(uid, out var dUid, out var fsComp))
                continue;

            if (!string.IsNullOrEmpty(serviceProtocol) && srvComp.ServerProtocols.Contains(serviceProtocol))
            {
                hostCompUid = cUid;
                hostDriveUid = dUid;
                hostFs = fsComp;
                return true;
            }

            if (fallbackDrive == null)
            {
                fallbackComp = cUid;
                fallbackDrive = dUid;
                fallbackFs = fsComp;
            }
        }

        if (fallbackDrive != null && fallbackFs != null)
        {
            hostCompUid = fallbackComp;
            hostDriveUid = fallbackDrive;
            hostFs = fallbackFs;
            return true;
        }

        var routerComp = _router.UnwrapComputerEntity(coreRouterHostOrComp);
        if (_fileSystem.TryFindFileSystem(routerComp, out var rDrive, out var rFs))
        {
            hostCompUid = routerComp;
            hostDriveUid = rDrive;
            hostFs = rFs;
            return true;
        }

        return false;
    }

    public EntityUid? GetServerDrive(EntityUid serverUid, out FileSystemComponent? fs)
    {
        fs = null;
        if (_fileSystem.TryFindFileSystem(serverUid, out var driveUid, out fs))
            return driveUid;

        return null;
    }

    public bool TryGetFirstOnlineRepositoryServer(
        EntityUid originCompUid,
        out EntityUid serverUid,
        out EntityUid serverCompUid,
        [NotNullWhen(true)] out EntityUid? serverDriveUid,
        [NotNullWhen(true)] out FileSystemComponent? serverFs,
        out string error)
    {
        serverUid = EntityUid.Invalid;
        serverCompUid = EntityUid.Invalid;
        serverDriveUid = null;
        serverFs = null;
        error = string.Empty;

        if (!_router.HasActiveNetworkCard(originCompUid))
        {
            error = "No active network card installed.";
            return false;
        }

        var originGuid = _router.EnsureDeviceGuid(originCompUid);
        if (_router.IsGuidBlacklisted(originCompUid, originGuid))
        {
            error = "Station network router unreachable or blacklisted.";
            return false;
        }

        if (_router.IsNetworkConnected(originCompUid))
        {
            var query = EntityQueryEnumerator<DataServerComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (IsDebugFallbackServer(uid))
                    continue;

                var cUid = _router.UnwrapComputerEntity(uid);
                if (!_router.IsRouterOperational(uid) || !_router.HasActiveNetworkCard(cUid))
                    continue;

                if (_fileSystem.TryFindFileSystem(uid, out var dUid, out var fsComp))
                {
                    serverUid = uid;
                    serverCompUid = cUid;
                    serverDriveUid = dUid;
                    serverFs = fsComp;
                    return true;
                }
            }
        }
#if DEBUG
        if (TryEnsureDebugRepositoryServer(out serverUid, out serverCompUid, out serverDriveUid, out serverFs))
            return true;
#else
        if (!_router.IsNetworkConnected(originCompUid))
        {
            error = "Station network router unreachable or blacklisted.";
            return false;
        }
#endif

        error = "No online Data Server reachable on station network.";
        return false;
    }

#if DEBUG
    private bool TryEnsureDebugRepositoryServer(
        out EntityUid serverUid,
        out EntityUid serverCompUid,
        [NotNullWhen(true)] out EntityUid? serverDriveUid,
        [NotNullWhen(true)] out FileSystemComponent? serverFs)
    {
        serverUid = EntityUid.Invalid;
        serverCompUid = EntityUid.Invalid;
        serverDriveUid = null;
        serverFs = null;

        if (_flushingEntities)
            return false;

        if (!Exists(_debugServerHost) || TerminatingOrDeleted(_debugServerHost))
        {
            _debugServerHost = Spawn(DebugDataServerPrototype, MapCoordinates.Nullspace);
            _meta.SetEntityName(_debugServerHost, DebugServerName);
            _power.SetNeedsPower(_debugServerHost, false);
            EnsureComp<DebugFallbackNetworkEntityComponent>(_debugServerHost).IsServer = true;
        }

        if (!TryComp<EmbeddedComputerHostComponent>(_debugServerHost, out var host) ||
            !_computer.EnsureComputerBoundToHost((_debugServerHost, host), out var compUid))
        {
            return false;
        }

        _debugServerComp = compUid;
        _meta.SetEntityName(compUid, DebugServerName);
        EnsureComp<DebugFallbackNetworkEntityComponent>(compUid).IsServer = true;
        if (TryComp<OxydComputerComponent>(compUid, out var comp))
        {
            comp.IsPowered = true;
            Dirty(compUid, comp);
            _computer.RecalculateHardwareStats(compUid, comp);
            if (!comp.IsBooted)
                _computer.RefreshComputerBootState(compUid, comp);
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fsComp))
            return false;

        EnsureDebugRepositoryCatalog(driveUid.Value, fsComp);

        serverUid = _debugServerHost;
        serverCompUid = compUid;
        serverDriveUid = driveUid;
        serverFs = fsComp;
        return true;
    }

    private void EnsureDebugRepositoryCatalog(EntityUid driveUid, FileSystemComponent fs)
    {
        (string Path, string Name, string ProtoId, string Title)[] catalog =
        [
            ("/bin/notekeeper.nte", "notekeeper", "NotekeeperProgram", "Notekeeper"),
            ("/bin/file_browser.nte", "file_browser", "OxydFileBrowserProgram", "File Browser"),
            ("/bin/nanotask.nte", "nanotask", "NanoTaskProgram", "Tasks"),
            ("/bin/crew_manifest.nte", "crew_manifest", "CrewManifestProgram", "Crew Manifest"),
            ("/bin/station_records.nte", "station_records", "StationRecordsProgram", "Station Records"),
            ("/bin/criminal_records.nte", "criminal_records", "CriminalRecordsProgram", "Criminal Records"),
            ("/bin/wanted_list.nte", "wanted_list", "WantedListProgram", "Wanted List"),
            ("/bin/cameras.nte", "cameras", "SurveillanceCameraProgram", "Surveillance Cameras"),
            ("/bin/news_reader.nte", "news_reader", "NewsReaderProgram", "News Reader"),
            ("/bin/netprobe.nte", "netprobe", "NetProbeProgram", "NetProbe"),
            ("/bin/logprobe.nte", "logprobe", "LogProbeProgram", "LogProbe"),
            ("/bin/packet_sniffer.nte", "packet_sniffer", "OxydPacketSnifferProgram", "Packet Sniffer"),
            ("/bin/medtek.nte", "medtek", "MedTekProgram", "MedTek"),
            ("/bin/astronav.nte", "astronav", "AstroNavProgram", "AstroNav"),
            ("/bin/access_encoder.nte", "access_encoder", "AccessEncoderProgram", "ID Access Encoder"),
            ("/bin/pda_os.nte", "pda_os", "OxydPdaOsProgram", "NTOS Pocket"),
            ("/bin/downloader.nte", "downloader", "OxydDownloaderProgram", "Downloader"),
        ];

        foreach (var entry in catalog)
        {
            if (fs.Files.ContainsKey(entry.Path))
                continue;

            _fileSystem.TryWriteFile(driveUid, new ProgramVirtualFile
            {
                Name = entry.Name,
                Extension = "nte",
                Directory = "/bin/",
                Size = 4096,
                ProgramId = entry.ProtoId,
                Title = entry.Title,
                RamUsageMb = 8
            }, fs);
        }
    }
#endif
}
