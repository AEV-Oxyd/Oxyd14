using Content.Server.PDA;
using System.Linq;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CartridgeLoader;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Cartridges;

public sealed partial class DownloaderCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private PdaSystem _pdaSystem = default!;

    private float _downloadTickTimer = 0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DownloaderCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<DownloaderCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _downloadTickTimer += frameTime;
        var shouldPushUi = false;
        if (_downloadTickTimer >= 0.75f)
        {
            _downloadTickTimer = 0f;
            shouldPushUi = true;
        }

        var query = EntityQueryEnumerator<DownloaderCartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveDownloadItemId == null)
                continue;

            comp.DownloadRemainingSeconds -= frameTime;

            var loaderUid = Transform(uid).ParentUid;
            if (!loaderUid.IsValid())
                continue;

            if (comp.DownloadRemainingSeconds > 0)
            {
                var pct = Math.Clamp((int)((1f - comp.DownloadRemainingSeconds / comp.DownloadTotalSeconds) * 100f), 0, 99);
                var itemName = comp.PendingItemName ?? comp.PendingProgramName ?? "package";
                var actionWord = comp.PendingProgramProtoId != null ? "Downloading & Installing" : "Downloading";
                comp.StatusMessage = $"{actionWord} '{itemName}'... {pct}% ({comp.DownloadRemainingSeconds:0.#}s remaining)";

                if (shouldPushUi)
                    UpdateUiState(uid, loaderUid, comp);
            }
            else
            {
                comp.ActiveDownloadItemId = null;
                if (comp.PendingProgramProtoId != null)
                {
                    if (TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
                    {
                        if (_cartridgeLoader.InstallProgram((loaderUid, loader), comp.PendingProgramProtoId))
                        {
                            var cleanName = (comp.PendingProgramName ?? "app").ToLowerInvariant().Replace(' ', '_').Replace("-", "_");
                            if (cleanName.EndsWith("_cartridge"))
                                cleanName = cleanName.Substring(0, cleanName.Length - "_cartridge".Length);

                            var progFile = new ProgramVirtualFile
                            {
                                Name = cleanName,
                                Extension = "bin",
                                Directory = "/bin/",
                                ProgramId = comp.PendingProgramProtoId,
                                Title = comp.PendingProgramName ?? cleanName,
                                Size = comp.PendingProgramSize,
                                IsReadOnly = false
                            };
                            _fileSystem.TryWriteFile(loaderUid, progFile);

                            comp.StatusMessage = $"Installed '{comp.PendingProgramName}' successfully! Available in Program List & /bin/.";
                            _pdaSystem.UpdatePdaUi(loaderUid);
                        }
                        else
                        {
                            comp.StatusMessage = "Installation failed: Loader slot error or incompatible cartridge.";
                        }
                    }
                    comp.PendingProgramProtoId = null;
                    comp.PendingProgramName = null;
                    comp.PendingProgramSize = 0;
                }
                else if (comp.PendingFile != null && TryComp<FileSystemComponent>(loaderUid, out var localFs))
                {
                    if (_fileSystem.TryWriteFile(loaderUid, comp.PendingFile, localFs))
                    {
                        comp.StatusMessage = $"Download complete: '{comp.PendingItemName}' ({comp.PendingFile.Size} B) -> {comp.PendingFile.FullPath}";
                        _pdaSystem.UpdatePdaUi(loaderUid);
                    }
                    else
                    {
                        comp.StatusMessage = "Download failed: Insufficient storage space on local device.";
                    }
                    comp.PendingFile = null;
                    comp.PendingItemName = null;
                }
                UpdateUiState(uid, loaderUid, comp);
            }
        }
    }

    private void OnUiReady(EntityUid uid, DownloaderCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, comp);
    }

    private void OnUiMessage(EntityUid uid, DownloaderCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not DownloaderUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        switch (msg.Action)
        {
            case DownloaderAction.Refresh:
                comp.StatusMessage = "Repository index updated from central servers.";
                break;

            case DownloaderAction.SelectCategory:
                comp.CurrentCategory = string.IsNullOrWhiteSpace(msg.Parameter) ? "All" : msg.Parameter;
                break;

            case DownloaderAction.DownloadItem:
                HandleDownload(loaderUid, comp, msg.Parameter);
                break;

            case DownloaderAction.InstallProgram:
                HandleInstall(loaderUid, comp, msg.Parameter);
                break;
        }

        UpdateUiState(uid, loaderUid, comp);
    }

    private void HandleDownload(EntityUid loaderUid, DownloaderCartridgeComponent comp, string itemId)
    {
        if (comp.ActiveDownloadItemId != null)
        {
            comp.StatusMessage = "A download is already in progress. Please wait for it to complete.";
            return;
        }

        if (!TryComp<FileSystemComponent>(loaderUid, out var localFs))
            return;

        var repoItems = BuildRepositoryItems(loaderUid);
        var item = repoItems.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
        {
            comp.StatusMessage = $"Item '{itemId}' not found in repository.";
            return;
        }

        // If the item is a software application, route directly to installation
        if (!string.IsNullOrEmpty(item.PrototypeId))
        {
            HandleInstall(loaderUid, comp, itemId);
            return;
        }

        var fullPath = item.RemotePath;
        var dir = fullPath.Substring(0, fullPath.LastIndexOf('/') + 1);
        var filenameWithExt = fullPath.Substring(fullPath.LastIndexOf('/') + 1);
        var parts = filenameWithExt.Split('.');
        var name = parts[0];
        var ext = parts.Length > 1 ? parts[1] : "dat";

        VirtualFile newFile;
        if (ext == "db")
        {
            var db = new DatabaseVirtualFile
            {
                Name = name,
                Extension = ext,
                Directory = dir,
                Size = item.Size,
                TableName = name,
                Data = new Dictionary<string, string>
                {
                    { "ServerSource", "CentralMainframe" },
                    { "SyncTimestamp", DateTime.UtcNow.ToString("O") },
                    { "Status", "Verified" }
                }
            };
            newFile = db;
        }
        else if (ext == "bp" || ext == "lathe")
        {
            var bp = new RecipeBlueprintFile
            {
                Name = name,
                Extension = ext,
                Directory = dir,
                Size = item.Size,
                Recipes = new List<ProtoId<LatheRecipePrototype>>(item.Recipes),
                Recipe = item.Recipes.Count > 0 ? (ProtoId<LatheRecipePrototype>?)item.Recipes[0] : null,
                RemainingUses = null,
                IsEncrypted = false,
                DrmLocked = false
            };
            if (bp.Size <= 0)
                bp.Size = bp.CalculateSize(_prototypeManager);
            newFile = bp;
        }
        else
        {
            var tf = new TextVirtualFile
            {
                Name = name,
                Extension = ext,
                Directory = dir,
                Size = item.Size,
                TextContent = $"--- {item.Name} ---\n{item.Description}\nSource: Station Central Mainframe\nVerified OK.",
                IsReadOnly = false
            };
            newFile = tf;
        }

        var duration = CalculateDownloadTime(loaderUid, item.Size);
        comp.ActiveDownloadItemId = itemId;
        comp.DownloadRemainingSeconds = duration;
        comp.DownloadTotalSeconds = duration;
        comp.PendingFile = newFile;
        comp.PendingItemName = item.Name;
        comp.StatusMessage = $"Connecting to Central Repository... Transferring '{item.Name}' [0%]";
    }

    private void HandleInstall(EntityUid loaderUid, DownloaderCartridgeComponent comp, string itemId)
    {
        if (comp.ActiveDownloadItemId != null)
        {
            comp.StatusMessage = "A download/install is already in progress. Please wait for it to complete.";
            return;
        }

        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader))
            return;

        var repoItems = BuildRepositoryItems(loaderUid);
        var item = repoItems.FirstOrDefault(i => i.Id == itemId);
        if (item == null || string.IsNullOrEmpty(item.PrototypeId))
        {
            comp.StatusMessage = $"Cannot install '{itemId}': Invalid program package.";
            return;
        }

        // Check if already installed
        foreach (var progUid in _cartridgeLoader.GetAllPrograms(loaderUid))
        {
            if (MetaData(progUid).EntityPrototype?.ID == item.PrototypeId)
            {
                comp.StatusMessage = $"Program '{item.Name}' is already installed!";
                return;
            }
        }

        var duration = CalculateDownloadTime(loaderUid, item.Size);
        comp.ActiveDownloadItemId = itemId;
        comp.DownloadRemainingSeconds = duration;
        comp.DownloadTotalSeconds = duration;
        comp.PendingProgramProtoId = item.PrototypeId;
        comp.PendingProgramName = item.Name;
        comp.PendingProgramSize = Math.Max(2048, item.Size);
        comp.StatusMessage = $"Connecting to Central Package Server... Downloading '{item.Name}' [0%]";
    }

    private float CalculateDownloadTime(EntityUid loaderUid, int sizeBytes)
    {
        TryComp<NetworkBandwidthComponent>(loaderUid, out var bw);
        var isSaturated = bw?.IsSaturated ?? false;

        // Base wireless connection and SSL/SSH handshake overhead (seconds)
        var handshake = 2.0f;

        // Effective PDA wireless throughput: ~8 KB/sec (8,192 bytes/sec)
        // If congested or network saturated, transfer rate drops to ~2.5 KB/sec (2,560 bytes/sec)
        var transferRateBps = isSaturated ? 2560f : 8192f;

        var transferDuration = sizeBytes / transferRateBps;

        // Total download time scales directly with file size, with a minimum of 2.5s for wireless handshake
        return Math.Max(2.5f, handshake + transferDuration);
    }

    private List<RepositoryItemDto> BuildRepositoryItems(EntityUid loaderUid)
    {
        TryComp<FileSystemComponent>(loaderUid, out var localFs);
        TryComp<CartridgeLoaderComponent>(loaderUid, out var loader);

        bool HasFile(string path) => localFs != null && localFs.Files.ContainsKey(path);
        bool HasProg(string proto) => _cartridgeLoader.GetAllPrograms(loaderUid).Any(p => MetaData(p).EntityPrototype?.ID == proto);

        return new List<RepositoryItemDto>
        {
            // Software & Cartridge Programs
            new(
                "pkg_manifest",
                "Crew Manifest Viewer",
                "Programs",
                "Real-time station crew roster and department assignment monitor.",
                "/bin/crew_manifest.bin",
                ComputerMetricsHelper.GetProgramSize("CrewManifestCartridge"),
                HasProg("CrewManifestCartridge"),
                HasProg("CrewManifestCartridge"),
                "CrewManifestCartridge"
            ),
            new(
                "pkg_notetask",
                "NanoTask Operations Planner",
                "Programs",
                "Personal workflow management tool and priority task tracker.",
                "/bin/nanotask.bin",
                ComputerMetricsHelper.GetProgramSize("NanoTaskCartridge"),
                HasProg("NanoTaskCartridge"),
                HasProg("NanoTaskCartridge"),
                "NanoTaskCartridge"
            ),
            new(
                "pkg_news",
                "Station News Network Reader",
                "Programs",
                "Nanotrasen news feed terminal client with live broadcast reception.",
                "/bin/newsreader.bin",
                ComputerMetricsHelper.GetProgramSize("NewsReaderCartridge"),
                HasProg("NewsReaderCartridge"),
                HasProg("NewsReaderCartridge"),
                "NewsReaderCartridge"
            ),

            // Databases & Records
            new(
                "db_manifest",
                "Station Manifest Database",
                "Databases",
                "Synchronized personnel roster and security clearances table.",
                "/records/manifest.db",
                57344,
                HasFile("/records/manifest.db"),
                false
            ),
            new(
                "db_records",
                "Personnel Security Cache",
                "Databases",
                "Employment records, arrest histories, and department certifications.",
                "/records/security_cache.db",
                98304,
                HasFile("/records/security_cache.db"),
                false
            ),

            // Engineering Schematics & Blueprints
            new(
                "bp_airlock",
                "Airlock Electronics Schematic",
                "Blueprints",
                "Standard and blast door electronics manufacturing schematics.",
                "/blueprints/airlock_schematic.bp",
                36864,
                HasFile("/blueprints/airlock_schematic.bp"),
                false,
                recipes: new() { "DoorElectronics", "FirelockElectronics" }
            ),
            new(
                "bp_solars",
                "Solar Tracker Electronics Blueprint",
                "Blueprints",
                "Engineering alignment circuitry for station photovoltaic solar arrays.",
                "/blueprints/solar_optimizer.bp",
                16384,
                HasFile("/blueprints/solar_optimizer.bp"),
                false,
                recipes: new() { "SolarTrackerElectronics" }
            ),
            new(
                "bp_security",
                "Security Restraints & Equipment",
                "Blueprints",
                "Standard Nanotrasen security equipment: Handcuffs, Security Bolas, and Security HUDs.",
                "/blueprints/security_gear.bp",
                32768,
                HasFile("/blueprints/security_gear.bp"),
                false,
                recipes: new() { "Handcuffs", "SecurityBola", "ClothingEyesHudSecurity" }
            ),

            // Station Operating Documentation
            new(
                "doc_sop",
                "Nanotrasen Standard Operating Procedures",
                "Documents",
                "Official guidelines on alert levels, containment, and command protocols.",
                "/notes/nanotrasen_sop.txt",
                2400,
                HasFile("/notes/nanotrasen_sop.txt"),
                false
            ),
            new(
                "doc_incident",
                "Security Incident Report Form",
                "Documents",
                "Standard investigation template for reporting station anomalies and crimes.",
                "/notes/incident_template.txt",
                480,
                HasFile("/notes/incident_template.txt"),
                false
            )
        };
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, DownloaderCartridgeComponent comp)
    {
        var serverName = "Station Central Server";
        var serverGuid = "MAIN-01";
        var isOnline = true;
        var bandwidthBps = 250000;

        // Try discovering physical DataServer on station
        var query = EntityQueryEnumerator<DataServerComponent, DeviceGuidComponent>();
        if (query.MoveNext(out var serverUid, out var dataServer, out var guidComp))
        {
            serverName = MetaData(serverUid).EntityName;
            serverGuid = guidComp.Guid;
            TryComp<NetworkBandwidthComponent>(serverUid, out var bw);
            bandwidthBps = bw?.MaxBps ?? 250000;
        }

        var allItems = BuildRepositoryItems(loaderUid);
        var visibleItems = allItems;
        if (comp.CurrentCategory != "All")
        {
            visibleItems = allItems.Where(i => i.Category.Equals(comp.CurrentCategory, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var state = new DownloaderUiState(
            serverName,
            serverGuid,
            isOnline,
            bandwidthBps,
            comp.StatusMessage,
            comp.CurrentCategory,
            visibleItems);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
