using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
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
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Content.Shared.CartridgeLoader.Cartridges.NotekeeperCartridgeSystem _notekeeperSystem = default!;

    private string? _viewedFileName;
    private string? _viewedFileContent;
    private bool _viewedFileIsOnDisk;
    private string? _viewedFilePath;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeActivatedEvent>(OnCartridgeActivated);
        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<FileBrowserCartridgeComponent, CartridgeRelayedEvent<FileSystemChangedEvent>>(OnRelayedFileSystemChanged);
    }

    private void OnCartridgeActivated(EntityUid uid, FileBrowserCartridgeComponent comp, ref CartridgeActivatedEvent args)
    {
        var loaderUid = args.Loader;
        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp) &&
            TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoaderComp))
        {
            EnsureProgramFiles(loaderUid, cLoaderComp, fsComp);
        }
        UpdateUiState(uid, loaderUid, comp);
    }

    private void OnRelayedFileSystemChanged(EntityUid uid, FileBrowserCartridgeComponent comp, ref CartridgeRelayedEvent<FileSystemChangedEvent> args)
    {
        var loaderUid = args.Loader.Owner;
        _pdaSystem.UpdatePdaUi(loaderUid);

        if (args.Loader.Comp.ActiveProgram == uid)
        {
            UpdateUiState(uid, loaderUid, comp);
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

    private bool TryGetDisk(EntityUid loaderUid, out EntityUid diskUid, [NotNullWhen(true)] out FileSystemComponent? diskFs, out string diskName)
    {
        diskUid = default;
        diskFs = null;
        diskName = "Data Disk";

        if (TryComp<PdaComponent>(loaderUid, out var pda) && pda.DiskSlot.Item is { } dUid && !TerminatingOrDeleted(dUid))
        {
            diskUid = dUid;
            diskName = MetaData(dUid).EntityName;
            return TryComp(dUid, out diskFs);
        }

        return false;
    }

    private void OnUiMessage(EntityUid uid, FileBrowserCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not FileBrowserUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        switch (msg.Action)
        {
            case FileBrowserAction.ChangeDirectory:
                if (msg.Path.StartsWith("/disk"))
                {
                    if (!TryGetDisk(loaderUid, out _, out _, out _))
                    {
                        comp.StatusMessage = "No disk inserted in drive.";
                        comp.CurrentDirectory = "/";
                        _viewedFileName = null;
                        _viewedFileContent = null;
                        _viewedFileIsOnDisk = false;
                        _viewedFilePath = null;
                        break;
                    }
                }
                comp.CurrentDirectory = string.IsNullOrWhiteSpace(msg.Path) ? "/" : msg.Path;
                _viewedFileName = null;
                _viewedFileContent = null;
                _viewedFileIsOnDisk = false;
                _viewedFilePath = null;
                break;

            case FileBrowserAction.ViewFile:
                if (msg.Path.StartsWith("/disk/") || comp.CurrentDirectory.StartsWith("/disk"))
                {
                    if (TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
                    {
                        var searchPath = ResolveDiskSearchPath(dFs, msg.Path);
                        if (_fileSystem.TryGetFile<VirtualFile>(dUid, searchPath, out var dvFile, dFs))
                        {
                            _viewedFileName = $"{dvFile.Name}.{dvFile.Extension}";
                            _viewedFileIsOnDisk = true;
                            _viewedFilePath = msg.Path;
                            SetPreviewContent(dvFile);
                        }
                    }
                    else
                    {
                        comp.StatusMessage = "Cannot view file: Disk has been removed.";
                    }
                }
                else if (TryComp<FileSystemComponent>(loaderUid, out var viewFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, msg.Path, out var vFile, viewFs))
                {
                    _viewedFileName = $"{vFile.Name}.{vFile.Extension}";
                    _viewedFileIsOnDisk = false;
                    _viewedFilePath = msg.Path;
                    SetPreviewContent(vFile);
                }
                break;

            case FileBrowserAction.CloseView:
                _viewedFileName = null;
                _viewedFileContent = null;
                _viewedFileIsOnDisk = false;
                _viewedFilePath = null;
                break;

            case FileBrowserAction.RunProgram:
                VirtualFile? runFile = null;
                if (msg.Path.StartsWith("/disk/") || comp.CurrentDirectory.StartsWith("/disk"))
                {
                    if (TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
                    {
                        var searchPath = ResolveDiskSearchPath(dFs, msg.Path);
                        _fileSystem.TryGetFile(dUid, searchPath, out runFile, dFs);
                    }
                }
                else if (TryComp<FileSystemComponent>(loaderUid, out var runFs))
                {
                    _fileSystem.TryGetFile(loaderUid, msg.Path, out runFile, runFs);
                }

                if (runFile != null)
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
                    else if (runFile is TextVirtualFile)
                    {
                        if (TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoader))
                        {
                            var notekeeperProg = _cartridgeLoader.GetAllPrograms(loaderUid)
                                .FirstOrDefault(p => HasComp<Content.Shared.CartridgeLoader.Cartridges.NotekeeperCartridgeComponent>(p));

                            if (notekeeperProg != default && TryComp<Content.Shared.CartridgeLoader.Cartridges.NotekeeperCartridgeComponent>(notekeeperProg, out var nkComp))
                            {
                                nkComp.CurrentFilePath = msg.Path;
                                Dirty(notekeeperProg, nkComp);
                                _cartridgeLoader.ActivateProgram((loaderUid, cLoader), notekeeperProg);
                                _notekeeperSystem.UpdateUiState(notekeeperProg, loaderUid, nkComp);
                                return;
                            }
                            else
                            {
                                comp.StatusMessage = "Notekeeper program not available to open text files.";
                            }
                        }
                    }
                    else
                    {
                        comp.StatusMessage = $"Cannot execute non-executable file '{runFile.Name}.{runFile.Extension}'.";
                    }
                }
                break;

            case FileBrowserAction.DeleteFile:
                if (msg.Path.StartsWith("/disk/") || comp.CurrentDirectory.StartsWith("/disk"))
                {
                    if (TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
                    {
                        var searchPath = ResolveDiskSearchPath(dFs, msg.Path);
                        if (_fileSystem.TryGetFile<VirtualFile>(dUid, searchPath, out var toDel, dFs))
                        {
                            if (toDel.IsReadOnly)
                            {
                                comp.StatusMessage = $"Cannot delete '{toDel.Name}.{toDel.Extension}': File is Read-Only.";
                            }
                            else if (_fileSystem.TryDeleteFile(dUid, searchPath, dFs))
                            {
                                comp.StatusMessage = $"Deleted '{toDel.Name}.{toDel.Extension}' from disk.";
                                if (_viewedFilePath == msg.Path)
                                {
                                    _viewedFileName = null;
                                    _viewedFileContent = null;
                                }
                            }
                            else
                            {
                                comp.StatusMessage = $"Failed to delete '{toDel.Name}.{toDel.Extension}' from disk.";
                            }
                        }
                        else
                        {
                            comp.StatusMessage = $"File not found on disk: '{msg.Path}'.";
                        }
                    }
                    else
                    {
                        comp.StatusMessage = "No disk inserted in drive.";
                    }
                }
                else if (TryComp<FileSystemComponent>(loaderUid, out var delFs))
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
                                if (_viewedFilePath == msg.Path)
                                {
                                    _viewedFileName = null;
                                    _viewedFileContent = null;
                                }
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

            case FileBrowserAction.CopyToDisk:
                if (!TryGetDisk(loaderUid, out var targetDiskUid, out var targetDiskFs, out var diskName))
                {
                    comp.StatusMessage = "Cannot copy to disk: No disk inserted in slot.";
                    break;
                }

                var copyPath = string.IsNullOrEmpty(msg.Path) ? _viewedFilePath ?? "" : msg.Path;
                if (TryComp<FileSystemComponent>(loaderUid, out var srcFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, copyPath, out var srcFile, srcFs))
                {
                    var clone = CloneVirtualFile(srcFile, "/disk/");
                    if (_fileSystem.TryWriteFile(targetDiskUid, clone, targetDiskFs))
                    {
                        comp.StatusMessage = $"Copied '{clone.Name}.{clone.Extension}' to disk '{diskName}'.";
                    }
                    else
                    {
                        comp.StatusMessage = $"Failed to copy to disk: Insufficient disk space (Need {clone.Size} B, Available: {targetDiskFs.MaxStorageBytes - targetDiskFs.UsedStorageBytes} B).";
                    }
                }
                else
                {
                    comp.StatusMessage = $"Source file '{copyPath}' not found.";
                }
                break;

            case FileBrowserAction.CopyToInternal:
                if (!TryGetDisk(loaderUid, out var srcDiskUid, out var srcDiskFs, out _))
                {
                    comp.StatusMessage = "Cannot copy from disk: No disk inserted in slot.";
                    break;
                }

                var diskSrcPath = string.IsNullOrEmpty(msg.Path) ? _viewedFilePath ?? "" : msg.Path;
                var resolvedDiskPath = ResolveDiskSearchPath(srcDiskFs, diskSrcPath);

                if (_fileSystem.TryGetFile<VirtualFile>(srcDiskUid, resolvedDiskPath, out var diskFile, srcDiskFs) &&
                    TryComp<FileSystemComponent>(loaderUid, out var pdaFs))
                {
                    var destDir = GetDefaultDirectoryForFile(diskFile);
                    var clone = CloneVirtualFile(diskFile, destDir);
                    if (_fileSystem.TryWriteFile(loaderUid, clone, pdaFs))
                    {
                        comp.StatusMessage = $"Copied '{clone.Name}.{clone.Extension}' to PDA storage ({destDir}).";
                        if (clone is ProgramVirtualFile pvf && TryComp<CartridgeLoaderComponent>(loaderUid, out var cLoader))
                        {
                            _cartridgeLoader.InstallProgram((loaderUid, cLoader), pvf.ProgramId);
                        }
                    }
                    else
                    {
                        comp.StatusMessage = $"Failed to copy to PDA: Insufficient internal storage.";
                    }
                }
                else
                {
                    comp.StatusMessage = $"Disk file '{diskSrcPath}' not found.";
                }
                break;

            case FileBrowserAction.CreateFile:
                var targetIsDisk = comp.CurrentDirectory.StartsWith("/disk");
                var targetEntity = loaderUid;
                FileSystemComponent? targetFs = null;

                if (targetIsDisk)
                {
                    if (TryGetDisk(loaderUid, out var tdUid, out targetFs, out _))
                        targetEntity = tdUid;
                    else
                    {
                        comp.StatusMessage = "No disk inserted in drive.";
                        break;
                    }
                }
                else
                {
                    TryComp(loaderUid, out targetFs);
                }

                if (targetFs != null)
                {
                    var count = targetFs.Files.Count(f => f.Value.Name.StartsWith("note_")) + 1;
                    var newName = $"note_{count}";
                    var newDir = targetIsDisk ? "/disk/" : "/notes/";
                    var newFile = new TextVirtualFile
                    {
                        Name = newName,
                        Extension = "txt",
                        Directory = newDir,
                        Size = 32,
                        IsReadOnly = false,
                        TextContent = $"Empty text document created on {(targetIsDisk ? "disk" : "PDA")}.\nReady for editing and transfers."
                    };

                    if (_fileSystem.TryWriteFile(targetEntity, newFile, targetFs))
                    {
                        comp.StatusMessage = $"Created '{newName}.txt' in {newDir}.";
                    }
                    else
                    {
                        comp.StatusMessage = "Failed to create file: Not enough storage space.";
                    }
                }
                break;

            case FileBrowserAction.EjectDisk:
                if (TryComp<PdaComponent>(loaderUid, out var ejectPdaComp) && ejectPdaComp.DiskSlot.HasItem)
                {
                    _itemSlots.TryEject(loaderUid, ejectPdaComp.DiskSlot, null, out _);
                    comp.StatusMessage = "Disk ejected.";
                    if (comp.CurrentDirectory.StartsWith("/disk"))
                        comp.CurrentDirectory = "/";
                    if (_viewedFileIsOnDisk)
                    {
                        _viewedFileName = null;
                        _viewedFileContent = null;
                        _viewedFileIsOnDisk = false;
                        _viewedFilePath = null;
                    }
                }
                break;
        }

        UpdateUiState(uid, loaderUid, comp);
    }

    private string ResolveDiskSearchPath(FileSystemComponent diskFs, string requestedPath)
    {
        if (diskFs.Files.ContainsKey(requestedPath))
            return requestedPath;

        if (requestedPath.StartsWith("/disk/"))
        {
            var stripped = requestedPath.Substring("/disk".Length);
            if (diskFs.Files.ContainsKey(stripped))
                return stripped;

            var baseName = System.IO.Path.GetFileName(requestedPath);
            foreach (var key in diskFs.Files.Keys)
            {
                if (System.IO.Path.GetFileName(key) == baseName)
                    return key;
            }
        }

        return requestedPath;
    }

    private void SetPreviewContent(VirtualFile vFile)
    {
        if (vFile is TextVirtualFile tf)
            _viewedFileContent = tf.TextContent;
        else if (vFile is DatabaseVirtualFile db)
            _viewedFileContent = string.Join("\n", db.Data.Select(kv => $"{kv.Key} = {kv.Value}"));
        else if (vFile is RecipeBlueprintFile rFile)
            _viewedFileContent = $"Recipe Blueprint: {rFile.Recipe}\nRemaining Uses: {(rFile.RemainingUses.HasValue ? rFile.RemainingUses.Value.ToString() : "Unlimited")}\nEncrypted: {rFile.IsEncrypted}\nDRM Locked: {rFile.DrmLocked}\n\nCan be manufactured at an autolathe or transferred to another disk/PDA.";
        else if (vFile is ProgramVirtualFile pvf)
            _viewedFileContent = $"Executable Program: {pvf.Title}\nProgram ID: {pvf.ProgramId}\nSize: {pvf.Size} bytes\nCPU Cost: {pvf.CpuUsagePercent}%\nRAM Cost: {pvf.RamUsageMb} MB";
        else
            _viewedFileContent = $"Binary File ({vFile.Size} bytes)";
    }

    private VirtualFile CloneVirtualFile(VirtualFile source, string targetDirectory)
    {
        if (source is TextVirtualFile tf)
        {
            return new TextVirtualFile
            {
                Name = tf.Name,
                Extension = tf.Extension,
                Directory = targetDirectory,
                Size = tf.Size,
                IsReadOnly = false,
                TextContent = tf.TextContent
            };
        }
        if (source is RecipeBlueprintFile rf)
        {
            return new RecipeBlueprintFile
            {
                Name = rf.Name,
                Extension = rf.Extension,
                Directory = targetDirectory,
                Size = rf.Size,
                IsReadOnly = false,
                Recipe = rf.Recipe,
                Recipes = new(rf.Recipes),
                RemainingUses = rf.RemainingUses,
                DrmLocked = rf.DrmLocked,
                IsEncrypted = rf.IsEncrypted,
                EncryptionKeyHash = rf.EncryptionKeyHash
            };
        }
        if (source is DatabaseVirtualFile db)
        {
            return new DatabaseVirtualFile
            {
                Name = db.Name,
                Extension = db.Extension,
                Directory = targetDirectory,
                Size = db.Size,
                IsReadOnly = false,
                TableName = db.TableName,
                Data = new Dictionary<string, string>(db.Data)
            };
        }
        if (source is ProgramVirtualFile pf)
        {
            return new ProgramVirtualFile
            {
                Name = pf.Name,
                Extension = pf.Extension,
                Directory = targetDirectory,
                Size = pf.Size,
                IsReadOnly = false,
                ProgramId = pf.ProgramId,
                Title = pf.Title,
                CpuUsagePercent = pf.CpuUsagePercent,
                RamUsageMb = pf.RamUsageMb
            };
        }

        return new TextVirtualFile
        {
            Name = source.Name,
            Extension = source.Extension,
            Directory = targetDirectory,
            Size = source.Size,
            IsReadOnly = false,
            TextContent = $"Binary content ({source.Size} bytes)"
        };
    }

    private string GetDefaultDirectoryForFile(VirtualFile file)
    {
        if (file is RecipeBlueprintFile) return "/blueprints/";
        if (file is TextVirtualFile) return "/notes/";
        if (file is DatabaseVirtualFile) return "/records/";
        if (file is ProgramVirtualFile) return "/bin/";
        return "/";
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
                    file is RecipeBlueprintFile rFile2 ? rFile2.RemainingUses : null,
                    isOnDisk: false));
            }
        }

        var hasDisk = TryGetDisk(loaderUid, out _, out var diskFs, out var diskName);
        if (hasDisk && diskFs != null)
        {
            foreach (var file in diskFs.Files.Values)
            {
                var diskDir = file.Directory.StartsWith("/disk") ? file.Directory : "/disk/";
                fileList.Add(new FileBrowserItemDto(
                    file.Name,
                    file.Extension,
                    diskDir,
                    file.Size,
                    file.IsReadOnly,
                    file is RecipeBlueprintFile rFile && rFile.IsEncrypted,
                    file is RecipeBlueprintFile rFile2 ? rFile2.RemainingUses : null,
                    isOnDisk: true));
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
            _viewedFileContent,
            viewedFileIsOnDisk: _viewedFileIsOnDisk,
            hasDisk: hasDisk,
            diskName: diskName,
            diskUsedBytes: diskFs?.UsedStorageBytes,
            diskTotalBytes: diskFs?.MaxStorageBytes);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
