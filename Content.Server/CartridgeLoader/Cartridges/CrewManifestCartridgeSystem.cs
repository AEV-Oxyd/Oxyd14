using System.Linq;
using Content.Server.CrewManifest;
using Content.Server.Station.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.CCVar;
using Content.Shared.CrewManifest;
using Content.Shared._Oxyd.Computers.FileSystem;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed partial class CrewManifestCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IConfigurationManager _configManager = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
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

    /// <summary>
    /// The ui messages received here get wrapped by a CartridgeMessageEvent and are relayed from the <see cref="CartridgeLoaderSystem"/>
    /// </summary>
    /// <remarks>
    /// The cartridge specific ui message event needs to inherit from the CartridgeMessageEvent
    /// </remarks>
    private void OnUiMessage(EntityUid uid, CrewManifestCartridgeComponent component, CartridgeMessageEvent args)
    {
        UpdateUiState(uid, GetEntity(args.LoaderUid), component);
    }

    /// <summary>
    /// This gets called when the ui fragment needs to be updated for the first time after activating
    /// </summary>
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
            // Display "loading failed" message
            var failureMessage = Loc.GetString("crew-manifest-cartridge-loading-failed");
            var failureState = new CrewManifestUiState(failureMessage, new CrewManifestEntries());
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, failureState);
            return;
        }

        var (stationName, entries) = _crewManifest.GetCrewManifest(owningStation.Value);

        // Cache real server manifest to local filesystem
        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp) && entries?.Entries != null)
        {
            var manifestData = new Dictionary<string, string>();
            foreach (var entry in entries.Entries)
            {
                manifestData[entry.Name] = $"{entry.JobTitle} [{entry.JobPrototype}]";
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

        var state = new CrewManifestUiState(stationName, entries);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
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
