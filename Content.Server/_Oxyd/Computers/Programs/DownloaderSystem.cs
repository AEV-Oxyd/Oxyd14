using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.Servers;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

[RegisterComponent]
public sealed partial class ActiveNetworkTransferComponent : Component
{
    public bool IsUpload;
    public EntityUid ComputerUid;
    public EntityUid LocalDriveUid;
    public EntityUid ServerCompUid;
    public EntityUid ServerDriveUid;
    public VirtualFile File = default!;
    public string TargetPath = string.Empty;
    public int TransferredBytes;
    public int TotalBytes;
}

/// <summary>
/// Active-component network file transfer manager for downloading executables (.nte), blueprints (.lathe),
/// databases (.db), and uploading local files across the station router using physical NIC & server limits.
/// </summary>
public sealed partial class DownloaderSystem : EntitySystem
{
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private DataServerSystem _dataServer = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<DownloaderProgramComponent> _dlQuery;

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();
        _dlQuery = GetEntityQuery<DownloaderProgramComponent>();

        SubscribeLocalEvent<DownloaderProgramComponent, ProgramStartupEvent>(OnStartup);
        SubscribeLocalEvent<DownloaderProgramComponent, ProgramActivatedEvent>(OnActivated);
        Subs.BuiEvents<DownloaderProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<DownloaderActionMessage>(OnUiAction);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveNetworkTransferComponent, DownloaderProgramComponent>();
        while (query.MoveNext(out var progUid, out var xfer, out var dlComp))
        {
            var isDebugServer = _dataServer.IsDebugFallbackServer(xfer.ServerCompUid);
            var blacklisted = _router.IsGuidBlacklisted(xfer.ComputerUid, _router.EnsureDeviceGuid(xfer.ComputerUid));
            if (!_router.HasActiveNetworkCard(xfer.ComputerUid) ||
                blacklisted ||
                (!isDebugServer && (!_router.IsNetworkConnected(xfer.ComputerUid) || !_router.IsRouterOperational(xfer.ServerCompUid))))
            {
                dlComp.StatusMessage = "Transfer aborted: Network connection lost.";
                RemCompDeferred<ActiveNetworkTransferComponent>(progUid);
                UpdateUi(progUid, xfer.ComputerUid, dlComp);
                continue;
            }

            var rateBps = xfer.IsUpload
                ? _bandwidth.GetEffectiveTransferRateBps(xfer.ComputerUid, xfer.ServerCompUid)
                : _bandwidth.GetEffectiveTransferRateBps(xfer.ServerCompUid, xfer.ComputerUid);

            if (rateBps <= 0)
            {
                dlComp.StatusMessage = "Transfer stalled: Zero available bandwidth.";
                RemCompDeferred<ActiveNetworkTransferComponent>(progUid);
                UpdateUi(progUid, xfer.ComputerUid, dlComp);
                continue;
            }

            var chunk = Math.Max(512, (int) (rateBps * frameTime));
            var delta = Math.Min(chunk, xfer.TotalBytes - xfer.TransferredBytes);
            xfer.TransferredBytes += delta;

            _bandwidth.RecordDataTransmitted(xfer.IsUpload ? xfer.ComputerUid : xfer.ServerCompUid, delta, isUpload: true);
            _bandwidth.RecordDataTransmitted(xfer.IsUpload ? xfer.ServerCompUid : xfer.ComputerUid, delta, isUpload: false);

            var pct = xfer.TotalBytes > 0 ? xfer.TransferredBytes * 100 / xfer.TotalBytes : 100;
            dlComp.StatusMessage = $"{(xfer.IsUpload ? "Uploading" : "Downloading")} {xfer.TargetPath}... {pct}% ({xfer.TransferredBytes}/{xfer.TotalBytes} B)";

            if (xfer.TransferredBytes >= xfer.TotalBytes)
            {
                CommitTransfer(progUid, xfer, dlComp);
                RemCompDeferred<ActiveNetworkTransferComponent>(progUid);
            }

            UpdateUi(progUid, xfer.ComputerUid, dlComp);
        }
    }

    private void CommitTransfer(EntityUid progUid, ActiveNetworkTransferComponent xfer, DownloaderProgramComponent dlComp)
    {
        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _router.EnsureDeviceGuid(xfer.IsUpload ? xfer.ComputerUid : xfer.ServerCompUid),
            TargetAddress = _router.EnsureDeviceGuid(xfer.IsUpload ? xfer.ServerCompUid : xfer.ComputerUid),
            Protocol = xfer.IsUpload ? "TCP/UPLOAD" : "TCP/DOWNLOAD",
            PayloadSummary = $"{(xfer.IsUpload ? "PUT" : "GET")} {xfer.TargetPath} ({xfer.TotalBytes} B)",
            ByteSize = xfer.TotalBytes,
            Timestamp = _timing.CurTime
        };
        _bandwidth.LogPacketToRouters(pkt, xfer.ComputerUid);

        var targetDrive = xfer.IsUpload ? xfer.ServerDriveUid : xfer.LocalDriveUid;
        if (Exists(targetDrive) && TryComp<FileSystemComponent>(targetDrive, out var fs))
        {
            var copy = xfer.File.Clone();
            if (_fileSystem.TryWriteFile(targetDrive, copy, fs))
            {
                _fileSystem.LogSystemActivity(targetDrive, xfer.IsUpload ? "UPLOAD" : "DOWNLOAD", $"Stored {xfer.TargetPath} ({xfer.TotalBytes} B)");
                dlComp.StatusMessage = $"{(xfer.IsUpload ? "Uploaded" : "Downloaded")} '{xfer.TargetPath}' successfully.";
                return;
            }
        }

        dlComp.StatusMessage = "Transfer failed: Target storage full or missing.";
    }

    private void OnStartup(Entity<DownloaderProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnActivated(Entity<DownloaderProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnUiAction(Entity<DownloaderProgramComponent> ent, ref DownloaderActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

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
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    private void BeginTransfer(EntityUid progUid, EntityUid compUid, DownloaderProgramComponent dlComp, string path, bool isUpload)
    {
        if (!_dataServer.TryGetFirstOnlineRepositoryServer(compUid, out _, out var srvCompUid, out var srvDriveUid, out var srvFs, out var netErr))
        {
            dlComp.StatusMessage = $"Network error: {netErr}";
            return;
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var localDriveUid, out var localFs))
        {
            dlComp.StatusMessage = "Transfer failed: No local hard drive installed.";
            return;
        }

        var srcFs = isUpload ? localFs : srvFs;
        if (!srcFs.Files.TryGetValue(path, out var sourceFile))
        {
            dlComp.StatusMessage = $"File '{path}' not found on {(isUpload ? "local drive" : "repository server")}.";
            return;
        }

        var active = EnsureComp<ActiveNetworkTransferComponent>(progUid);
        active.IsUpload = isUpload;
        active.ComputerUid = compUid;
        active.LocalDriveUid = localDriveUid.Value;
        active.ServerCompUid = srvCompUid;
        active.ServerDriveUid = srvDriveUid.Value;
        active.File = sourceFile.Clone();
        active.TargetPath = path;
        active.TransferredBytes = 0;
        active.TotalBytes = Math.Max(64, sourceFile.Size);

        dlComp.StatusMessage = $"Connecting to {MetaData(srvCompUid).EntityName} for {(isUpload ? "upload" : "download")}...";
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
                    $"{file.Directory} | {(file.Size < 1024 ? $"{file.Size} B" : $"{file.Size / 1024} KB")}",
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
