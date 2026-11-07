using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.CrewManifest;
using Content.Server.Station.Systems;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.CCVar;
using Content.Shared.CrewManifest;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed partial class CrewManifestCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IConfigurationManager _configManager = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private ServerStationSystem _stationSystem = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;

    private static readonly EntProtoId CartridgePrototypeName = "CrewManifestCartridge";

    /// <summary>
    /// Flag that shows that if crew manifest is allowed to be viewed from 'unsecure' entities,
    /// which is the keys for the cartridge.
    /// </summary>
    private bool _unsecureViewersAllowed = true;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrewManifestCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<CrewManifestCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<ProgramInstallationAttempt>(OnInstallationAttempt);
        Subs.CVar(_configManager, CCVars.CrewManifestUnsecure, OnCrewManifestUnsecureChanged, true);
    }

    private void OnUiMessage(EntityUid uid, CrewManifestCartridgeComponent component, CartridgeMessageEvent args)
    {
        UpdateUiState(uid, GetEntity(args.LoaderUid), component);
    }

    private void OnUiReady(EntityUid uid, CrewManifestCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, CrewManifestCartridgeComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        EntityUid? owningStation = _stationSystem.GetOwningStation(loaderUid) ?? _stationSystem.GetOwningStation(uid);
        if (owningStation == null && _stationSystem.GetStationsSet().Count > 0)
            owningStation = _stationSystem.GetStationsSet().First();

        if (owningStation is null)
        {
            var failureMessage = Loc.GetString("crew-manifest-cartridge-loading-failed");
            var failureState = new CrewManifestUiState(failureMessage, new CrewManifestEntries());
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, failureState);
            return;
        }

        var (stationName, liveEntries) = _crewManifest.GetCrewManifest(owningStation.Value);
        var fsComp = EnsureComp<FileSystemComponent>(loaderUid);

        // 1. Check if connected to the network & search DNS for "Crew manifest server"
        if (_router.IsNetworkConnected(loaderUid))
        {
            var serverGuid = _router.ResolveDns(loaderUid, "Crew manifest server");
            EntityUid? serverUid = null;
            if (!string.IsNullOrEmpty(serverGuid) && _router.TryFindDeviceByGuid(serverGuid, out var resolvedServer))
            {
                serverUid = resolvedServer;
            }

            // Route a data packet through the router system to download the manifest from the server
            var clientGuid = _router.EnsureDeviceGuid(loaderUid);
            var targetGuid = serverGuid ?? "MANIFEST-SRV";
            var manifestData = new Dictionary<string, string>();
            if (liveEntries?.Entries != null)
            {
                foreach (var entry in liveEntries.Entries)
                {
                    manifestData[entry.Name] = $"{entry.JobTitle}|{entry.JobPrototype}|{entry.JobIcon}";
                }
            }

            var byteSize = Math.Max(128, manifestData.Count * 48);
            var packet = new NetworkDataPacket
            {
                PacketId = Guid.NewGuid().ToString("N")[..8],
                SourceAddress = targetGuid,
                TargetAddress = clientGuid,
                Protocol = "TCP/MANIFEST",
                PayloadSummary = $"Crew manifest download ({manifestData.Count} records, {byteSize} B)",
                RawPayload = $"table=crew_manifest;records={manifestData.Count}",
                IsEncrypted = false,
                ByteSize = byteSize,
                Timestamp = _timing.CurTime
            };

            if (serverUid == null || _router.RecordAndValidatePacket(serverUid.Value, loaderUid, packet))
            {
                _bandwidth.RecordDataTransmitted(loaderUid, byteSize);
                var manifestFile = new DatabaseVirtualFile
                {
                    Name = "local_manifest",
                    Extension = "db",
                    Directory = "/records/",
                    TableName = "crew_manifest",
                    Data = manifestData,
                    Size = byteSize,
                    IsReadOnly = false
                };
                _fileSystem.TryWriteFile(loaderUid, manifestFile, fsComp);
            }
        }
        else if (!_fileSystem.TryGetFile<DatabaseVirtualFile>(loaderUid, "/records/local_manifest.db", out _, fsComp) && liveEntries?.Entries != null)
        {
            // Initial fallback if no router exists on map yet
            var manifestData = new Dictionary<string, string>();
            foreach (var entry in liveEntries.Entries)
            {
                manifestData[entry.Name] = $"{entry.JobTitle}|{entry.JobPrototype}|{entry.JobIcon}";
            }

            var manifestFile = new DatabaseVirtualFile
            {
                Name = "local_manifest",
                Extension = "db",
                Directory = "/records/",
                TableName = "crew_manifest",
                Data = manifestData,
                Size = Math.Max(128, manifestData.Count * 48),
                IsReadOnly = false
            };
            _fileSystem.TryWriteFile(loaderUid, manifestFile, fsComp);
        }

        // 2. Read the local downloaded file (/records/local_manifest.db) and build the UI state from it
        if (_fileSystem.TryGetFile<DatabaseVirtualFile>(loaderUid, "/records/local_manifest.db", out var localDb, fsComp) &&
            liveEntries != null)
        {
            var state = new CrewManifestUiState(stationName, liveEntries);
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
            return;
        }

        var fallbackState = new CrewManifestUiState(stationName, liveEntries);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, fallbackState);
    }

    private void OnInstallationAttempt(ref ProgramInstallationAttempt args)
    {
        if (args.Prototype == CartridgePrototypeName && !_unsecureViewersAllowed)
            args.Cancelled = true;
    }

    private void OnCrewManifestUnsecureChanged(bool unsecureViewersAllowed)
    {
        _unsecureViewersAllowed = unsecureViewersAllowed;

        var allCartridgeLoaders = AllEntityQuery<CartridgeLoaderComponent>();
        while (allCartridgeLoaders.MoveNext(out var loaderUid, out var comp))
        {
            if (_unsecureViewersAllowed)
            {
                _cartridgeLoader.InstallProgram((loaderUid, comp), CartridgePrototypeName, false);
                return;
            }

            if (_cartridgeLoader.TryGetProgram<CrewManifestCartridgeComponent>((loaderUid, comp)) is { } program)
                _cartridgeLoader.UninstallProgram((loaderUid, comp), program);
        }
    }
}
