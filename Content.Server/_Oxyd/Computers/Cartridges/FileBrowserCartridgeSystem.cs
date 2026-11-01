using System.Linq;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Server.PDA;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Cartridges;

public sealed partial class FileBrowserCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private PdaSystem _pdaSystem = default!;
    [Dependency] private IGameTiming _timing = default!;

    private string? _viewedFileName;
    private string? _viewedFileContent;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<FileSystemComponent, FileSystemChangedEvent>(OnFileSystemChanged);
    }

    private void OnFileSystemChanged(EntityUid uid, FileSystemComponent comp, ref FileSystemChangedEvent args)
    {
        _pdaSystem.UpdatePdaUi(uid);

        if (!TryComp<CartridgeLoaderComponent>(uid, out var loader))
            return;

        if (loader.ActiveProgram != null &&
            TryComp<FileBrowserCartridgeComponent>(loader.ActiveProgram.Value, out var browserComp))
        {
            UpdateUiState(loader.ActiveProgram.Value, uid, browserComp);
        }
    }

    private void OnUiReady(EntityUid uid, FileBrowserCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        var loaderUid = args.Loader;
        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp) &&
            TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoaderComp))
        {
            EnsureProgramFiles(loaderUid, cLoaderComp, fsComp);
        }
        UpdateUiState(uid, loaderUid, comp);
    }

    private void OnUiMessage(EntityUid uid, FileBrowserCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not FileBrowserUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        switch (msg.Action)
        {
            case FileBrowserAction.ChangeDirectory:
                comp.CurrentDirectory = string.IsNullOrWhiteSpace(msg.Path) ? "/" : msg.Path;
                _viewedFileName = null;
                _viewedFileContent = null;
                break;

            case FileBrowserAction.ViewFile:
                if (TryComp<FileSystemComponent>(loaderUid, out var viewFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, msg.Path, out var vFile, viewFs))
                {
                    _viewedFileName = $"{vFile.Name}.{vFile.Extension}";
                    if (vFile is TextVirtualFile tf)
                        _viewedFileContent = tf.TextContent;
                    else if (vFile is DatabaseVirtualFile db)
                        _viewedFileContent = string.Join("\n", db.Data.Select(kv => $"{kv.Key} = {kv.Value}"));
                    else if (vFile is RecipeBlueprintFile rFile)
                        _viewedFileContent = $"Recipe: {rFile.Recipe}\nRemaining Uses: {rFile.RemainingUses}\nEncrypted: {rFile.IsEncrypted}\nDRM Locked: {rFile.DrmLocked}";
                    else if (vFile is ProgramVirtualFile pvf)
                        _viewedFileContent = $"Executable Program: {pvf.Title}\nProgram ID: {pvf.ProgramId}\nSize: {pvf.Size} bytes\nCPU Cost: {pvf.CpuUsagePercent}%\nRAM Cost: {pvf.RamUsageMb} MB";
                    else
                        _viewedFileContent = $"Binary File ({vFile.Size} bytes)";
                }
                break;

            case FileBrowserAction.RunProgram:
                if (TryComp<FileSystemComponent>(loaderUid, out var runFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, msg.Path, out var runFile, runFs))
                {
                    if (runFile is ProgramVirtualFile prog)
                    {
                        if (TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoader))
                        {
                            var targetProgram = _cartridgeLoader.GetAllPrograms(loaderUid)
                                .FirstOrDefault(p => MetaData(p).EntityPrototype?.ID == prog.ProgramId);

                            if (targetProgram == default)
                            {
                                if (_cartridgeLoader.InstallProgram((loaderUid, cLoader), prog.ProgramId))
                                {
                                    targetProgram = _cartridgeLoader.GetAllPrograms(loaderUid)
                                        .FirstOrDefault(p => MetaData(p).EntityPrototype?.ID == prog.ProgramId);
                                }
                            }

                            if (targetProgram != default)
                            {
                                _cartridgeLoader.ActivateProgram((loaderUid, cLoader), targetProgram);
                                return;
                            }
                            else
                            {
                                comp.StatusMessage = $"Program cartridge '{prog.ProgramId}' not loaded.";
                            }
                        }
                    }
                    else
                    {
                        comp.StatusMessage = $"Cannot execute non-executable file '{runFile.Name}.{runFile.Extension}'.";
                    }
                }
                break;

            case FileBrowserAction.CloseView:
                _viewedFileName = null;
                _viewedFileContent = null;
                break;

            case FileBrowserAction.DeleteFile:
                if (TryComp<FileSystemComponent>(loaderUid, out var delFs))
                {
                    if (_fileSystem.TryGetFile<VirtualFile>(loaderUid, msg.Path, out var toDel, delFs))
                    {
                        if (toDel.IsReadOnly)
                        {
                            comp.StatusMessage = $"Cannot delete '{msg.Path}': File is Read-Only.";
                        }
                        else
                        {
                            if (toDel is ProgramVirtualFile prog)
                            {
                                if (TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoader))
                                {
                                    var targetProgram = _cartridgeLoader.GetAllPrograms(loaderUid)
                                        .FirstOrDefault(p => MetaData(p).EntityPrototype?.ID == prog.ProgramId);

                                    if (targetProgram != default)
                                    {
                                        _cartridgeLoader.UninstallProgram((loaderUid, cLoader), targetProgram);
                                    }
                                }

                                if (TryComp<PdaComponent>(loaderUid, out var pdaComp))
                                {
                                    pdaComp.OpenPrograms.RemoveAll(p => !Exists(p) || EntityManager.IsQueuedForDeletion(p) || MetaData(p).EntityPrototype?.ID == prog.ProgramId);
                                }
                            }

                            if (_fileSystem.TryDeleteFile(loaderUid, msg.Path, delFs))
                            {
                                comp.StatusMessage = $"Successfully deleted local file '{msg.Path}'.";
                                _pdaSystem.UpdatePdaUi(loaderUid);
                            }
                            else
                            {
                                comp.StatusMessage = $"Failed to delete '{msg.Path}'.";
                            }
                        }
                    }
                    else
                    {
                        comp.StatusMessage = $"File not found: '{msg.Path}'.";
                    }
                }
                break;
        }

        UpdateUiState(uid, loaderUid, comp);
    }

    private void EnsureProgramFiles(EntityUid loaderUid, CartridgeLoaderComponent loader, FileSystemComponent fs)
    {
        foreach (var prog in _cartridgeLoader.GetAllPrograms(loaderUid))
        {
            var meta = MetaData(prog);
            if (meta.EntityPrototype == null)
                continue;

            var protoId = meta.EntityPrototype.ID;
            var cleanName = meta.EntityPrototype.Name.ToLowerInvariant().Replace(' ', '_').Replace("-", "_");
            if (cleanName.EndsWith("_cartridge"))
                cleanName = cleanName.Substring(0, cleanName.Length - "_cartridge".Length);
            var fullPath = $"/bin/{cleanName}.bin";

            if (!fs.Files.TryGetValue(fullPath, out var existingFile) || existingFile.Size == 2048)
            {
                var title = CompOrNull<CartridgeComponent>(prog)?.ProgramName is { } nameLoc ? Loc.GetString(nameLoc) : meta.EntityPrototype.Name;
                var progFile = new ProgramVirtualFile
                {
                    Name = cleanName,
                    Extension = "bin",
                    Directory = "/bin/",
                    ProgramId = protoId,
                    Title = title,
                    Size = ComputerMetricsHelper.GetProgramSize(protoId),
                    IsReadOnly = false
                };
                _fileSystem.TryWriteFile(loaderUid, progFile, fs);
            }
        }
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, FileBrowserCartridgeComponent comp)
    {
        var fileList = new List<FileBrowserItemDto>();

        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp))
        {
            foreach (var file in fsComp.Files.Values)
            {
                fileList.Add(new FileBrowserItemDto(
                    file.Name,
                    file.Extension,
                    file.Directory,
                    file.Size,
                    file.IsReadOnly,
                    file is RecipeBlueprintFile rFile && rFile.IsEncrypted,
                    file is RecipeBlueprintFile rFile2 ? rFile2.RemainingUses : null));
            }
        }

        TryComp<PdaComponent>(loaderUid, out var pda);
        var openCount = pda != null && pda.OpenPrograms.Count > 0 ? pda.OpenPrograms.Count : 1;
        TryComp<ComputerStatsComponent>(loaderUid, out var stats);
        TryComp<NetworkBandwidthComponent>(loaderUid, out var bw);

        ComputerMetricsHelper.CalculateMetrics(
            openCount,
            stats,
            fsComp,
            bw,
            out var cpuPercent,
            out var ramUsed,
            out var ramTotal,
            out var storageUsed,
            out var storageTotal,
            out var isSaturated,
            out var bandwidthBps,
            curTime: _timing.CurTime.TotalSeconds);

        var deviceGuid = _guidSystem.GetGuid(loaderUid);

        var state = new FileBrowserUiState(
            comp.CurrentDirectory,
            fileList,
            deviceGuid,
            cpuPercent,
            ramUsed,
            ramTotal,
            storageUsed,
            storageTotal,
            bandwidthBps,
            isSaturated,
            comp.StatusMessage,
            _viewedFileName,
            _viewedFileContent);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
