using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Servers;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Servers;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Network file transfer application for downloading executables (.nte), blueprints (.lathe),
/// databases (.db), and uploading local files across the station router's shared per-router transfer queue.
/// </summary>
public sealed partial class DownloaderSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<DataServerComponent> _dataServerQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<DownloaderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<DownloaderActionMessage>(OnUiAction);
        });
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<DownloaderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnActivated(Entity<DownloaderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnTerminated(Entity<DownloaderProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        _bandwidth.CancelTransfersForProgram(ent.Owner, "Downloader closed.");
    }

    private void OnUiAction(Entity<DownloaderProgramComponent> ent, ref DownloaderActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out var blockReason))
        {
            ent.Comp.StatusMessage = blockReason;
            UpdateUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        switch (args.Action)
        {
            case DownloaderAction.Refresh:
                ent.Comp.StatusMessage = "Refreshed repository index.";
                break;

            case DownloaderAction.SelectCategory:
                ent.Comp.CurrentCategory = string.IsNullOrWhiteSpace(args.Parameter) ? "All" : args.Parameter;
                break;

            case DownloaderAction.DownloadItem:
                BeginTransfer(ent.Owner, compUid, ent.Comp, args.Parameter, isUpload: false);
                break;

            case DownloaderAction.UploadFile:
                BeginTransfer(ent.Owner, compUid, ent.Comp, args.Parameter, isUpload: true);
                break;

            case DownloaderAction.CancelTransfer:
                var cancelled = _bandwidth.CancelTransfersForProgram(ent.Owner, "Transfer cancelled by user.");
                ent.Comp.StatusMessage = cancelled > 0 ? "Transfer cancelled." : "No active transfer to cancel.";
                break;
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void BeginTransfer(EntityUid progUid, EntityUid compUid, DownloaderProgramComponent dlComp, string path, bool isUpload)
    {
        if (!_dataServer.TryGetFirstOnlineRepositoryServer(compUid, out var srvHostUid, out var srvCompUid, out var srvDriveUid, out var srvFs, out var netErr))
        {
            dlComp.StatusMessage = $"Network error: {netErr}";
            return;
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var localDriveUid, out var localFs))
        {
            dlComp.StatusMessage = "Transfer failed: No local hard drive installed.";
            return;
        }

        var cleanPath = SharedFileSystemSystem.NormalizePath(path);
        var srcFs = isUpload ? localFs : srvFs;
        if (!srcFs.Files.TryGetValue(cleanPath, out var sourceFile))
        {
            dlComp.StatusMessage = $"File '{cleanPath}' not found on {(isUpload ? "local drive" : "repository server")}.";
            return;
        }

        if (_fileSystem.IsCopyProtectedBlueprint(sourceFile))
        {
            dlComp.StatusMessage = "Transfer refused: encrypted recipe files are copy-protected and disk-only.";
            return;
        }

        // Security: disallow unauthenticated overwriting of existing server system/database files via public uploads.
        if (isUpload && srvFs.Files.TryGetValue(cleanPath, out var existingSrvFile))
        {
            if (existingSrvFile.IsReadOnly ||
                existingSrvFile is ProgramVirtualFile or DatabaseVirtualFile ||
                cleanPath.StartsWith("/bin/", StringComparison.OrdinalIgnoreCase) ||
                cleanPath.StartsWith("/records/", StringComparison.OrdinalIgnoreCase))
            {
                dlComp.StatusMessage = $"Upload refused: '{cleanPath}' is protected on the repository server.";
                return;
            }
        }

        var isDebugServer = _dataServer.IsDebugFallbackServer(srvCompUid);
        var router = _router.FindNearestRouter(compUid) ?? _router.FindNearestRouter(srvCompUid);
        if (router == null)
        {
            dlComp.StatusMessage = "Transfer failed: no operational station router reachable.";
            return;
        }

        // Cancel any previous in-flight transfer from this Downloader instance before enqueueing a new one.
        _bandwidth.CancelTransfersForProgram(progUid, "Superseded by new transfer.");

        var srcComp = isUpload ? compUid : srvCompUid;
        var dstComp = isUpload ? srvCompUid : compUid;
        var targetDrive = isUpload ? srvDriveUid!.Value : localDriveUid!.Value;
        var totalBytes = Math.Max(64, sourceFile.Size);
        var fileClone = sourceFile.Clone();

        var editableEntries = new Dictionary<string, string>();
        var isEditable = false;
        if (fileClone is DatabaseVirtualFile dbFile)
        {
            isEditable = true;
            foreach (var (k, v) in dbFile.Data)
            {
                if (!k.StartsWith("__"))
                    editableEntries[k] = v;
            }
        }
        else if (fileClone is TextVirtualFile txtFile)
        {
            isEditable = true;
            editableEntries["content"] = txtFile.TextContent;
        }

        var pktMeta = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _router.EnsureDeviceGuid(srcComp),
            TargetAddress = _router.EnsureDeviceGuid(dstComp),
            Protocol = isUpload ? "TCP/UPLOAD" : "TCP/DOWNLOAD",
            PayloadSummary = $"{(isUpload ? "PUT" : "GET")} {cleanPath} ({ComputerSizeFormat.FormatStorageQb(totalBytes)})",
            IsPlayerRelevant = isEditable,
            IsEditable = isEditable,
            FilePath = cleanPath,
            EditableEntries = editableEntries,
            ByteSize = totalBytes,
            Timestamp = _timing.CurTime
        };

        var xfer = new QueuedRouterTransfer
        {
            Protocol = pktMeta.Protocol,
            TargetPath = cleanPath,
            Description = $"{(isUpload ? "Upload" : "Download")} {cleanPath}",
            IsUpload = isUpload,
            SourceCompUid = srcComp,
            TargetCompUid = dstComp,
            InitiatorProgramUid = progUid,
            TotalBytes = totalBytes,
            AllowDebugFallback = isDebugServer,
            PayloadFile = fileClone,
            PacketMetadata = pktMeta,
            OnProgress = t =>
            {
                if (!Exists(progUid) || !TryComp<DownloaderProgramComponent>(progUid, out var c))
                    return;
                var pct = t.TotalBytes > 0 ? t.TransferredBytes * 100 / t.TotalBytes : 100;
                c.StatusMessage = $"{(t.IsUpload ? "Uploading" : "Downloading")} {t.TargetPath}... {pct}% ({ComputerSizeFormat.FormatStorageQb(t.TransferredBytes)} / {ComputerSizeFormat.FormatStorageQb(t.TotalBytes)})";
                UpdateUi(progUid, compUid, c);
            },
            OnCompleted = t =>
            {
                if (!Exists(progUid) || !TryComp<DownloaderProgramComponent>(progUid, out var c))
                    return;

                if (Exists(targetDrive) && TryComp<FileSystemComponent>(targetDrive, out var fs) && t.PayloadFile != null)
                {
                    if (_fileSystem.TryWriteFile(targetDrive, t.PayloadFile.Clone(), fs))
                    {
                        _fileSystem.LogSystemActivity(targetDrive, t.IsUpload ? "UPLOAD" : "DOWNLOAD", $"Stored {t.TargetPath} ({ComputerSizeFormat.FormatStorageQb(t.TotalBytes)})");
                        c.StatusMessage = $"{(t.IsUpload ? "Uploaded" : "Downloaded")} '{t.TargetPath}' successfully.";
                    }
                    else
                    {
                        c.StatusMessage = "Transfer failed: Target storage full or read-only.";
                    }
                }
                else
                {
                    c.StatusMessage = "Transfer failed: Target storage missing.";
                }

                UpdateUi(progUid, compUid, c);
            },
            OnAborted = (t, reason) =>
            {
                if (!Exists(progUid) || !TryComp<DownloaderProgramComponent>(progUid, out var c))
                    return;

                c.StatusMessage = reason;
                UpdateUi(progUid, compUid, c);
            }
        };

        if (!_bandwidth.EnqueueRouterTransfer(router.Value, xfer))
        {
            dlComp.StatusMessage = "Transfer failed: router offline.";
            return;
        }

        dlComp.StatusMessage = $"Queued {(isUpload ? "upload" : "download")} of {cleanPath} on router ({totalBytes} B)...";
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, DownloaderProgramComponent dlComp)
    {
        var hasNic = _router.HasActiveNetworkCard(compUid);
        var serverOnline = _dataServer.TryGetFirstOnlineRepositoryServer(compUid, out var srvUid, out var srvCompUid, out var srvDriveUid, out var srvFs, out var err);
        var routerOnline = hasNic && (_router.IsNetworkConnected(compUid) || (serverOnline && _dataServer.IsDebugFallbackServer(srvCompUid)));

        var stats = CompOrNull<ComputerStatsComponent>(compUid);
        var downBps = stats?.NetworkBandwidthBps ?? 0;
        var upBps = stats?.NetworkUploadBandwidthBps ?? 0;

        _fileSystem.TryFindFileSystem(compUid, out var localDriveUid, out var localFs);

        var items = new List<RepositoryItemDto>();
        if (serverOnline && srvFs != null)
        {
            foreach (var (path, file) in srvFs.Files)
            {
                if (_fileSystem.IsCopyProtectedBlueprint(file))
                    continue;

                var cat = file switch
                {
                    ProgramVirtualFile => "Programs",
                    RecipeBlueprintFile => "Blueprints",
                    DatabaseVirtualFile => "Databases",
                    _ => "Documents"
                };

                if (dlComp.CurrentCategory != "All" && !dlComp.CurrentCategory.Equals(cat, StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloaded = localFs != null && localFs.Files.ContainsKey(path);
                var protoId = file is ProgramVirtualFile pf ? pf.ProgramId : string.Empty;
                var title = file is ProgramVirtualFile pfTitle && !string.IsNullOrEmpty(pfTitle.Title) ? pfTitle.Title : $"{file.Name}.{file.Extension}";

                items.Add(new RepositoryItemDto(
                    path,
                    title,
                    cat,
                    $"{file.Directory} | {ComputerSizeFormat.FormatStorageQb(file.Size)}",
                    path,
                    file.Size,
                    downloaded,
                    protoId));
            }
        }
        else if (dlComp.StatusMessage == "Ready")
        {
            dlComp.StatusMessage = err;
        }

        var localUploadables = new List<LocalUploadCandidateDto>();
        if (localFs != null)
        {
            foreach (var (path, file) in localFs.Files)
            {
                if (_fileSystem.IsCopyProtectedBlueprint(file))
                    continue;

                if (file is TextVirtualFile or RecipeBlueprintFile or TaskListVirtualFile or DatabaseVirtualFile)
                    localUploadables.Add(new LocalUploadCandidateDto(path, file.Size));
            }
        }

        var state = new DownloaderUiState(
            serverOnline ? MetaData(srvUid).EntityName : "OFFLINE",
            serverOnline ? _router.EnsureDeviceGuid(srvCompUid) : "---",
            routerOnline,
            serverOnline,
            downBps,
            upBps,
            dlComp.StatusMessage,
            dlComp.CurrentCategory,
            items,
            localUploadables);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
