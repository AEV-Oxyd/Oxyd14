using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.Power.EntitySystems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.GameTicking;
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

        if (args.Data.ServiceName == "ManifestService" &&
            _fileSystem.TryGetFile<DatabaseVirtualFile>(driveUid.Value, "/records/manifest.db", out var manifestFile, fs))
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
            ("/bin/wanted_list.nte", "wanted_list", "WantedListProgram", "Wanted List"),
            ("/bin/news_reader.nte", "news_reader", "NewsReaderProgram", "News Reader"),
            ("/bin/netprobe.nte", "netprobe", "NetProbeProgram", "NetProbe"),
            ("/bin/logprobe.nte", "logprobe", "LogProbeProgram", "LogProbe"),
            ("/bin/packet_sniffer.nte", "packet_sniffer", "OxydPacketSnifferProgram", "Packet Sniffer"),
            ("/bin/medtek.nte", "medtek", "MedTekProgram", "MedTek"),
            ("/bin/astronav.nte", "astronav", "AstroNavProgram", "AstroNav"),
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
