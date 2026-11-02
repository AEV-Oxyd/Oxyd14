using Content.Server.PDA;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeviceNetwork.Components;
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
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;

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
        if (_downloadTickTimer >= 0.15f)
        {
            _downloadTickTimer = 0f;
            shouldPushUi = true;
        }

        // 1. Gather all active downloading cartridges and count how many clients share each server/router
        var activeDownloads = new List<(EntityUid CartUid, DownloaderCartridgeComponent Comp, EntityUid LoaderUid, EntityUid? ServerUid)>();
        var serverActiveCount = new Dictionary<EntityUid, int>();
        var fallbackCount = 0;

        var query = EntityQueryEnumerator<DownloaderCartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveDownloadItemId == null)
                continue;

            var loaderUid = Transform(uid).ParentUid;
            if (!loaderUid.IsValid())
                continue;

            EntityUid? serverUid = null;
            if (comp.SourceServerNetEntity != null && TryGetEntity(comp.SourceServerNetEntity.Value, out var sUid) && Exists(sUid))
            {
                serverUid = sUid;
            }
            else
            {
                serverUid = GetSourceServer(loaderUid);
                if (serverUid != null)
                    comp.SourceServerNetEntity = GetNetEntity(serverUid.Value);
            }

            if (serverUid != null)
            {
                serverActiveCount[serverUid.Value] = serverActiveCount.GetValueOrDefault(serverUid.Value, 0) + 1;
            }
            else
            {
                fallbackCount++;
            }

            activeDownloads.Add((uid, comp, loaderUid, serverUid));
        }

        if (activeDownloads.Count == 0)
            return;

        // 2. Process each download with dynamically split server/router bandwidth
        foreach (var (cartUid, comp, loaderUid, serverUid) in activeDownloads)
        {
            float serverShareBps;
            int clientCount;

            if (serverUid != null)
            {
                clientCount = serverActiveCount.GetValueOrDefault(serverUid.Value, 1);
                var serverBw = CompOrNull<NetworkBandwidthComponent>(serverUid.Value);
                var serverCapacity = (float)(serverBw?.MaxBps ?? 250000);
                if (serverBw?.IsSaturated ?? false)
                    serverCapacity *= 0.25f;

                // Dynamically divide available server throughput among all active clients
                serverShareBps = serverCapacity / clientCount;
            }
            else
            {
                clientCount = Math.Max(1, fallbackCount);
                serverShareBps = 250000f / clientCount;
            }

            // Client device network adapter constraint
            var clientBw = CompOrNull<NetworkBandwidthComponent>(loaderUid);
            var clientCapacity = (float)(clientBw?.MaxBps ?? 128000);
            if (clientBw?.IsSaturated ?? false)
                clientCapacity *= 0.25f;

            var effectiveBps = Math.Min(serverShareBps, clientCapacity);
            effectiveBps = Math.Max(1000f, effectiveBps); // at least 1 KB/s crawl so downloads do not freeze completely

            if (comp.DownloadTotalBytes <= 0)
                comp.DownloadTotalBytes = 4096;

            var bytesThisFrame = effectiveBps * frameTime;
            comp.DownloadedBytes += bytesThisFrame;

            // Transmit throughput across network adapter queues
            if (serverUid != null)
                _bandwidth.RecordDataTransmitted(serverUid.Value, (int)bytesThisFrame);
            _bandwidth.RecordDataTransmitted(loaderUid, (int)bytesThisFrame);

            var clientGuid = CompOrNull<DeviceGuidComponent>(loaderUid)?.Guid ?? $"DEV:{loaderUid}";
            var serverGuid = serverUid != null ? (CompOrNull<DeviceGuidComponent>(serverUid.Value)?.Guid ?? $"SRV:{serverUid.Value}") : "CENTRAL-SRV";
            var itemName = comp.PendingItemName ?? comp.PendingProgramName ?? "package";
            var pct = Math.Clamp((int)((comp.DownloadedBytes / comp.DownloadTotalBytes) * 100f), 0, 99);

            // Log data packets for the file transfer chunk
            if (shouldPushUi || comp.DownloadedBytes >= comp.DownloadTotalBytes)
            {
                var chunkSize = (int)Math.Max(1, bytesThisFrame);
                _bandwidth.StorePacket(loaderUid, serverGuid, clientGuid, "HTTP/DOWNLOAD", $"Chunk: '{itemName}' ({chunkSize} B, {pct}%)", chunkSize);
                if (serverUid != null)
                    _bandwidth.StorePacket(serverUid.Value, serverGuid, clientGuid, "HTTP/DOWNLOAD", $"Chunk: '{itemName}' ({chunkSize} B, {pct}%)", chunkSize);
                _bandwidth.LogPacketToRouters(serverGuid, clientGuid, "HTTP/DOWNLOAD", $"Chunk: '{itemName}' ({chunkSize} B, {pct}%)", chunkSize);
            }

            var remainingBytes = Math.Max(0f, comp.DownloadTotalBytes - comp.DownloadedBytes);
            comp.DownloadRemainingSeconds = remainingBytes / effectiveBps;

            if (comp.DownloadedBytes < comp.DownloadTotalBytes)
            {
                var actionWord = comp.PendingProgramProtoId != null ? "Downloading & Installing" : "Downloading";
                var speedKb = effectiveBps / 1024f;

                if (clientCount > 1)
                {
                    comp.StatusMessage = $"{actionWord} '{itemName}'... {pct}% ({speedKb:0.0} KB/s [{clientCount} clients sharing link], {comp.DownloadRemainingSeconds:0.#}s remaining)";
                }
                else
                {
                    comp.StatusMessage = $"{actionWord} '{itemName}'... {pct}% ({speedKb:0.0} KB/s, {comp.DownloadRemainingSeconds:0.#}s remaining)";
                }

                if (shouldPushUi)
                    UpdateUiState(cartUid, loaderUid, comp);
            }
            else
            {
                var totalBytes = comp.DownloadTotalBytes;
                _bandwidth.StorePacket(loaderUid, serverGuid, clientGuid, "HTTP/COMPLETE", $"Transfer verified: '{itemName}' ({totalBytes} B)", 128);
                if (serverUid != null)
                    _bandwidth.StorePacket(serverUid.Value, serverGuid, clientGuid, "HTTP/COMPLETE", $"Transfer verified: '{itemName}' ({totalBytes} B)", 128);
                _bandwidth.LogPacketToRouters(serverGuid, clientGuid, "HTTP/COMPLETE", $"Transfer verified: '{itemName}' ({totalBytes} B)", 128);

                comp.ActiveDownloadItemId = null;
                comp.DownloadedBytes = 0f;
                comp.DownloadTotalBytes = 0;
                comp.SourceServerNetEntity = null;

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
                    if (comp.PendingFile is RecipeBlueprintFile rbp && !rbp.IsEncrypted && !rbp.DrmLocked && rbp.Recipes.Count > 1)
                    {
                        var allSuccess = true;
                        foreach (var r in rbp.Recipes)
                        {
                            var singleBp = new RecipeBlueprintFile
                            {
                                Name = r.Id.ToLowerInvariant(),
                                Extension = "lathe",
                                Directory = "/blueprints/",
                                Recipe = r,
                                Recipes = new() { r },
                                Size = 8192,
                                IsEncrypted = false,
                                DrmLocked = false
                            };
                            if (!_fileSystem.TryWriteFile(loaderUid, singleBp, localFs))
                                allSuccess = false;
                        }
                        comp.StatusMessage = allSuccess
                            ? $"Download complete: '{comp.PendingItemName}' (unpacked {rbp.Recipes.Count} blueprints)"
                            : "Download partially failed: Insufficient storage space.";
                        _pdaSystem.UpdatePdaUi(loaderUid);
                    }
                    else if (_fileSystem.TryWriteFile(loaderUid, comp.PendingFile, localFs))
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
                UpdateUiState(cartUid, loaderUid, comp);
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

        var sourceServer = GetSourceServer(loaderUid);
        comp.SourceServerNetEntity = sourceServer.HasValue ? GetNetEntity(sourceServer.Value) : null;
        comp.DownloadTotalBytes = Math.Max(2048, item.Size);
        comp.DownloadedBytes = 0f;

        var clientGuid = CompOrNull<DeviceGuidComponent>(loaderUid)?.Guid ?? $"DEV:{loaderUid}";
        var serverGuid = sourceServer != null ? (CompOrNull<DeviceGuidComponent>(sourceServer.Value)?.Guid ?? $"SRV:{sourceServer.Value}") : "CENTRAL-SRV";
        _bandwidth.StorePacket(loaderUid, clientGuid, serverGuid, "HTTP/REQ", $"GET {item.RemotePath} ({item.Size} B)", 128);
        if (sourceServer != null)
            _bandwidth.StorePacket(sourceServer.Value, clientGuid, serverGuid, "HTTP/REQ", $"GET {item.RemotePath} ({item.Size} B)", 128);
        _bandwidth.LogPacketToRouters(clientGuid, serverGuid, "HTTP/REQ", $"GET {item.RemotePath} ({item.Size} B)", 128);

        var duration = CalculateDownloadTime(loaderUid, comp.DownloadTotalBytes, sourceServer);
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

        var sourceServer = GetSourceServer(loaderUid);
        comp.SourceServerNetEntity = sourceServer.HasValue ? GetNetEntity(sourceServer.Value) : null;
        var pkgSize = Math.Max(2048, item.Size);
        comp.DownloadTotalBytes = pkgSize;
        comp.DownloadedBytes = 0f;

        var clientGuid = CompOrNull<DeviceGuidComponent>(loaderUid)?.Guid ?? $"DEV:{loaderUid}";
        var serverGuid = sourceServer != null ? (CompOrNull<DeviceGuidComponent>(sourceServer.Value)?.Guid ?? $"SRV:{sourceServer.Value}") : "CENTRAL-SRV";
        _bandwidth.StorePacket(loaderUid, clientGuid, serverGuid, "HTTP/REQ", $"INSTALL {item.Name} ({pkgSize} B)", 128);
        if (sourceServer != null)
            _bandwidth.StorePacket(sourceServer.Value, clientGuid, serverGuid, "HTTP/REQ", $"INSTALL {item.Name} ({pkgSize} B)", 128);
        _bandwidth.LogPacketToRouters(clientGuid, serverGuid, "HTTP/REQ", $"INSTALL {item.Name} ({pkgSize} B)", 128);

        var duration = CalculateDownloadTime(loaderUid, pkgSize, sourceServer);
        comp.ActiveDownloadItemId = itemId;
        comp.DownloadRemainingSeconds = duration;
        comp.DownloadTotalSeconds = duration;
        comp.PendingProgramProtoId = item.PrototypeId;
        comp.PendingProgramName = item.Name;
        comp.PendingProgramSize = pkgSize;
        comp.StatusMessage = $"Connecting to Central Package Server... Downloading '{item.Name}' [0%]";
    }

    private EntityUid? GetSourceServer(EntityUid loaderUid)
    {
        // 1. Check if there is an online physical DataServer on station
        var query = EntityQueryEnumerator<DataServerComponent, DeviceGuidComponent>();
        while (query.MoveNext(out var serverUid, out _, out _))
        {
            return serverUid;
        }

        // 2. Check for any NetworkBandwidth router or server
        var bwQuery = EntityQueryEnumerator<NetworkBandwidthComponent, DeviceNetworkComponent>();
        while (bwQuery.MoveNext(out var bwUid, out _, out _))
        {
            if (bwUid != loaderUid)
                return bwUid;
        }

        return null;
    }

    private float CalculateDownloadTime(EntityUid loaderUid, int sizeBytes, EntityUid? sourceServer = null)
    {
        var availableBps = 250000f; // 250 KB/s (~244 KiB/s) baseline network speed

        // Look up server network bandwidth if online
        var serverUid = sourceServer ?? GetSourceServer(loaderUid);
        if (serverUid != null && TryComp<NetworkBandwidthComponent>(serverUid.Value, out var serverBw) && serverBw.MaxBps > 0)
        {
            availableBps = serverBw.MaxBps;
            if (serverBw.IsSaturated)
                availableBps *= 0.25f;
        }

        // Bound by client network bandwidth capacity
        if (TryComp<NetworkBandwidthComponent>(loaderUid, out var clientBw) && clientBw.MaxBps > 0)
        {
            availableBps = Math.Min(availableBps, clientBw.MaxBps);
            if (clientBw.IsSaturated)
                availableBps *= 0.25f;
        }

        availableBps = Math.Max(10000f, availableBps);
        var transferDuration = (float)sizeBytes / availableBps;

        // Minimal realistic network handshake latency (50ms)
        return Math.Max(0.15f, 0.05f + transferDuration);
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

            // Engineering Schematics & Blueprints (1 file per unencrypted prototype design)
            new(
                "bp_door",
                "Airlock Electronics Blueprint",
                "Blueprints",
                "Standard airlock door electronics manufacturing schematic.",
                "/blueprints/door_electronics.lathe",
                8192,
                HasFile("/blueprints/door_electronics.lathe"),
                false,
                recipes: new() { "DoorElectronics" }
            ),
            new(
                "bp_firelock",
                "Firelock Electronics Blueprint",
                "Blueprints",
                "Emergency firelock door electronics manufacturing schematic.",
                "/blueprints/firelock_electronics.lathe",
                8192,
                HasFile("/blueprints/firelock_electronics.lathe"),
                false,
                recipes: new() { "FirelockElectronics" }
            ),
            new(
                "bp_solars",
                "Solar Tracker Electronics Blueprint",
                "Blueprints",
                "Engineering alignment circuitry for station photovoltaic solar arrays.",
                "/blueprints/solar_tracker.lathe",
                8192,
                HasFile("/blueprints/solar_tracker.lathe"),
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
