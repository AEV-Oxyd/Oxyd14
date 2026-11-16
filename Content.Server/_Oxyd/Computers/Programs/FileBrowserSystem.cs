using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;

namespace Content.Server._Oxyd.Computers.Programs;

public sealed partial class FileBrowserSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private EntityQuery<ProgramComponent> _programQuery;

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();

        SubscribeLocalEvent<FileBrowserProgramComponent, ProgramStartupEvent>(OnStartup);
        SubscribeLocalEvent<FileBrowserProgramComponent, ProgramActivatedEvent>(OnActivated);
        SubscribeLocalEvent<FileBrowserProgramComponent, ProgramDeviceLinkReceivedEvent>(OnDeviceLink);
        Subs.BuiEvents<FileBrowserProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<FileBrowserActionMessage>(OnUiAction);
        });
    }

    private void OnStartup(Entity<FileBrowserProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnActivated(Entity<FileBrowserProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnDeviceLink(Entity<FileBrowserProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        UpdateUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private bool TryGetInsertedDisk(EntityUid compUid, out EntityUid diskUid, out FileSystemComponent? diskFs, out string diskName)
    {
        diskUid = EntityUid.Invalid;
        diskFs = null;
        diskName = "Data Disk";

        if (!TryComp<OxydComputerComponent>(compUid, out var comp))
            return false;

        if (comp.HostMachine is { } hostUid &&
            TryComp<PdaComponent>(hostUid, out var pda) &&
            pda.ContainedDisk is { } pDisk &&
            TryComp<FileSystemComponent>(pDisk, out diskFs))
        {
            diskUid = pDisk;
            diskName = MetaData(pDisk).EntityName;
            return true;
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var modSlot) &&
            modSlot.Item is { } mDisk &&
            TryComp<FileSystemComponent>(mDisk, out diskFs))
        {
            diskUid = mDisk;
            diskName = MetaData(mDisk).EntityName;
            return true;
        }

        return false;
    }

    private void OnUiAction(Entity<FileBrowserProgramComponent> ent, ref FileBrowserActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fsComp);

        switch (args.Action)
        {
            case FileBrowserAction.ChangeDirectory:
                ent.Comp.CurrentDirectory = string.IsNullOrWhiteSpace(args.Path) ? "/" : args.Path;
                ent.Comp.ViewedFilePath = null;
                break;

            case FileBrowserAction.ViewFile:
                ent.Comp.ViewedFilePath = args.Path;
                ent.Comp.ViewedFileIsOnDisk = ent.Comp.CurrentDirectory == "/disk/";
                break;

            case FileBrowserAction.CloseView:
                ent.Comp.ViewedFilePath = null;
                break;

            case FileBrowserAction.DeleteFile:
                if (ent.Comp.CurrentDirectory == "/disk/" && TryGetInsertedDisk(compUid, out var delDisk, out var delDiskFs, out _))
                {
                    ent.Comp.StatusMessage = _fileSystem.TryDeleteFile(delDisk, args.Path, delDiskFs)
                        ? $"Deleted disk file '{args.Path}'."
                        : $"Cannot delete '{args.Path}'.";
                }
                else if (driveUid != null && fsComp != null)
                {
                    ent.Comp.StatusMessage = _fileSystem.TryDeleteFile(driveUid.Value, args.Path, fsComp)
                        ? $"Deleted '{args.Path}'."
                        : $"Cannot delete '{args.Path}' (read-only or missing).";
                }
                break;

            case FileBrowserAction.RunProgram:
                if (_computer.TryLaunchExecutable(compUid, args.Path, out _, out var err, args.Actor))
                    ent.Comp.StatusMessage = $"Launched '{args.Path}'.";
                else
                    ent.Comp.StatusMessage = err;
                break;

            case FileBrowserAction.CopyToDisk:
                if (driveUid != null && fsComp != null &&
                    _fileSystem.TryGetFile<VirtualFile>(driveUid.Value, args.Path, out var srcFile, fsComp) &&
                    TryGetInsertedDisk(compUid, out var dstDisk, out var dstDiskFs, out _))
                {
                    var clone = srcFile.Clone();
                    clone.IsReadOnly = false;
                    ent.Comp.StatusMessage = _fileSystem.TryWriteFile(dstDisk, clone, dstDiskFs)
                        ? $"Copied '{args.Path}' to disk."
                        : "Copy failed: Disk full or read-only.";
                }
                else
                {
                    ent.Comp.StatusMessage = "No writable data disk inserted.";
                }
                break;

            case FileBrowserAction.CopyToInternal:
                if (driveUid != null && fsComp != null &&
                    TryGetInsertedDisk(compUid, out var srcDisk, out var srcDiskFs, out _) &&
                    _fileSystem.TryGetFile<VirtualFile>(srcDisk, args.Path, out var diskFile, srcDiskFs))
                {
                    var clone = diskFile.Clone();
                    clone.IsReadOnly = false;
                    ent.Comp.StatusMessage = _fileSystem.TryWriteFile(driveUid.Value, clone, fsComp)
                        ? $"Copied '{args.Path}' to hard drive."
                        : "Copy failed: Hard drive full.";
                }
                else
                {
                    ent.Comp.StatusMessage = "Copy failed: Missing hard drive or file.";
                }
                break;

            case FileBrowserAction.CreateFile:
                if (driveUid != null && fsComp != null)
                {
                    var count = 1;
                    while (_fileSystem.TryGetFile<TextVirtualFile>(driveUid.Value, $"/notes/note_{count}.txt", out _, fsComp))
                        count++;

                    var newFile = new TextVirtualFile
                    {
                        Name = $"note_{count}",
                        Extension = "txt",
                        Directory = "/notes/",
                        TextContent = "Created in File Browser.",
                        Size = 128
                    };
                    ent.Comp.StatusMessage = _fileSystem.TryWriteFile(driveUid.Value, newFile, fsComp)
                        ? $"Created /notes/{newFile.Name}.txt"
                        : "Insufficient drive space.";
                }
                break;

            case FileBrowserAction.EjectDisk:
                if (TryComp<OxydComputerComponent>(compUid, out var oxyd) && oxyd.HostMachine is { } hostUid)
                    _itemSlots.TryEject(hostUid, PdaComponent.PdaDiskSlotId, args.Actor, out _);
                break;
        }

        UpdateUi(ent.Owner, compUid, ent.Comp);
    }

    public void UpdateUi(EntityUid progUid, EntityUid compUid, FileBrowserProgramComponent comp)
    {
        var files = new List<FileBrowserItemDto>();
        var hasHdd = _fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fsComp);

        if (hasHdd && fsComp != null)
        {
            foreach (var f in fsComp.Files.Values)
            {
                int? uses = f is RecipeBlueprintFile bp ? bp.RemainingUses : null;
                var enc = f is RecipeBlueprintFile bpEnc && bpEnc.IsEncrypted;
                files.Add(new FileBrowserItemDto(f.Name, f.Extension, f.Directory, f.Size, f.IsReadOnly, enc, uses, isOnDisk: false));
            }
        }

        var hasDisk = TryGetInsertedDisk(compUid, out var diskUid, out var diskFs, out var diskName);
        if (hasDisk && diskFs != null)
        {
            foreach (var f in diskFs.Files.Values)
            {
                int? uses = f is RecipeBlueprintFile bp ? bp.RemainingUses : null;
                var enc = f is RecipeBlueprintFile bpEnc && bpEnc.IsEncrypted;
                files.Add(new FileBrowserItemDto(f.Name, f.Extension, f.Directory, f.Size, f.IsReadOnly, enc, uses, isOnDisk: true));
            }
        }

        string? viewedName = null;
        string? viewedContent = null;
        if (!string.IsNullOrEmpty(comp.ViewedFilePath))
        {
            VirtualFile? targetFile = null;
            if (comp.ViewedFileIsOnDisk && diskFs != null)
                diskFs.Files.TryGetValue(comp.ViewedFilePath, out targetFile);
            else if (fsComp != null)
                fsComp.Files.TryGetValue(comp.ViewedFilePath, out targetFile);

            if (targetFile != null)
            {
                viewedName = $"{targetFile.Name}.{targetFile.Extension}";
                viewedContent = FormatFileContent(targetFile);
            }
        }

        var stats = CompOrNull<ComputerStatsComponent>(compUid);
        var bw = CompOrNull<NetworkBandwidthComponent>(compUid);

        var state = new FileBrowserUiState(
            comp.CurrentDirectory,
            files,
            _guid.GetGuid(compUid),
            stats?.CurrentCpuLoadPercent ?? 0f,
            stats?.UsedRamMb ?? 0,
            stats?.RamMb ?? 0,
            fsComp?.UsedStorageBytes ?? 0,
            fsComp?.MaxStorageBytes ?? 0,
            stats?.NetworkBandwidthBps ?? 0,
            bw?.IsSaturated ?? false,
            comp.StatusMessage,
            hasHdd,
            viewedName,
            viewedContent,
            comp.ViewedFileIsOnDisk,
            hasDisk,
            hasDisk ? diskName : null,
            diskFs?.UsedStorageBytes,
            diskFs?.MaxStorageBytes);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    private static string FormatFileContent(VirtualFile f)
    {
        return f switch
        {
            TextVirtualFile tf => tf.TextContent,
            ProgramVirtualFile pf => $"NTOS Executable (.nte)\nProgram Prototype: {pf.ProgramId}\nTitle: {pf.Title}\nRAM Footprint: {pf.RamUsageMb} MB\nCPU Budget: {pf.CpuUsagePercent:0.#}%",
            RecipeBlueprintFile bf => $"Autolathe Blueprint (.lathe)\nRecipes: {string.Join(", ", bf.GetAllRecipes())}\nEncrypted: {bf.IsEncrypted}\nRemaining DRM Uses: {bf.RemainingUses?.ToString() ?? "Unlimited"}",
            DatabaseVirtualFile df => $"Database Table: {df.TableName}\nEntries: {df.Data.Count}\n" + string.Join("\n", df.Data.Select(kv => $"  {kv.Key}: {kv.Value}")),
            TaskListVirtualFile tk => $"NanoTask Checklist ({tk.Tasks.Count} items)\n" + string.Join("\n", tk.Tasks.Select(t => $"  [{(t.IsTaskDone ? "x" : " ")}] #{t.Id} {t.Description} -> {t.TaskIsFor}")),
            _ => $"{f.Name}.{f.Extension} ({f.Size} B)"
        };
    }
}
