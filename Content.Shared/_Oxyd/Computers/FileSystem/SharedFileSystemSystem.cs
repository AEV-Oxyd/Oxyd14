using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Computers.FileSystem;

public partial class SharedFileSystemSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _compFactory = default!;

    private const string DefaultLogHeader = "=== NTOS 1.4™ SYSTEM AUDIT LOG ===\n----------------------------------------";
    private const int MaxLogEntries = 148;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<FileSystemComponent> ent, ref ComponentStartup args)
    {
        InitializeFileTimestamps(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<FileSystemComponent> ent, ref MapInitEvent args)
    {
        InitializeFileTimestamps(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<FileSystemComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ent.Comp.UsedStorageBytes = ent.Comp.NetworkedHeaders.Sum(h => h.Size);
    }

    public static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return "/";

        var cleaned = rawPath.Trim().Replace('\\', '/');
        var segments = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            if (seg == ".")
                continue;

            if (seg == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(seg);
        }

        return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
    }

    public static string NormalizeDirectory(string rawDir)
    {
        var norm = NormalizePath(rawDir);
        return norm.EndsWith('/') ? norm : norm + "/";
    }

    private void InitializeFileTimestamps(EntityUid uid, FileSystemComponent comp)
    {
        if (comp.Files.Count == 0)
            return;

        var curTime = _timing.CurTime;
        foreach (var file in comp.Files.Values)
        {
            file.Directory = NormalizeDirectory(file.Directory);
            file.Size = file.CalculateRealSize(_proto, _compFactory);

            if (file.LastModified == TimeSpan.Zero && curTime > TimeSpan.Zero)
                file.LastModified = curTime;
        }

        RecalculateStorageUsage(comp);
        UpdateNetworkedHeaders(uid, comp);
    }

    /// <summary>
    /// Encrypted or DRM/limited-use recipe blueprint files are hardware-locked to physical recipe disks
    /// and cannot be copied, transferred, uploaded, or duplicated onto computer hard drives.
    /// </summary>
    public bool IsCopyProtectedBlueprint(VirtualFile file)
    {
        return file is RecipeBlueprintFile bp &&
               (bp.IsEncrypted ||
                bp.DrmLocked ||
                bp.RemainingUses != null ||
                !string.IsNullOrWhiteSpace(bp.EncryptionKeyHash));
    }

    /// <summary>
    /// Refreshes storage usage, modification timestamp, networked headers, and change notifications
    /// after an in-place mutation of an existing file (such as decrementing recipe disk DRM uses).
    /// </summary>
    public void NotifyFileModified(EntityUid uid, string fullPath, FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        var normPath = NormalizePath(fullPath);
        if (comp.Files.TryGetValue(normPath, out var file))
        {
            file.Size = file.CalculateRealSize(_proto, _compFactory);
            file.LastModified = _timing.CurTime;
        }

        RecalculateStorageUsage(comp);
        UpdateNetworkedHeaders(uid, comp);
        var ev = new FileSystemChangedEvent(normPath);
        RaiseLocalEvent(uid, ref ev, broadcast: true);
    }

    public void RecalculateStorageUsage(FileSystemComponent comp)
    {
        if (comp.Files.Count > 0)
            comp.UsedStorageBytes = comp.Files.Values.Sum(f => f.Size);
        else
            comp.UsedStorageBytes = comp.NetworkedHeaders.Sum(h => h.Size);
    }

    public bool TryFindFileSystem(EntityUid entity, [NotNullWhen(true)] out EntityUid? driveUid, [NotNullWhen(true)] out FileSystemComponent? fsComp)
    {
        driveUid = null;
        fsComp = null;

        if (!Exists(entity))
            return false;

        // If passed a program entity, resolve via its parent computer first.
        if (TryComp<ProgramComponent>(entity, out var prog) &&
            prog.Computer is { } progComputer &&
            Exists(progComputer))
        {
            return TryFindFileSystem(progComputer, out driveUid, out fsComp);
        }

        // If passed a host device, resolve its encapsulated computer entity first.
        if (TryComp<EmbeddedComputerHostComponent>(entity, out var host) &&
            host.ComputerEntity is { } hostedComputer &&
            Exists(hostedComputer))
        {
            return TryFindFileSystem(hostedComputer, out driveUid, out fsComp);
        }

        // If passed a computer entity, only resolve its physically installed Hard Drive item entity.
        if (TryComp<OxydComputerComponent>(entity, out var comp))
        {
            if (comp.StorageWireCut)
                return false;

            if (TryComp<Hardware.ComputerStatsComponent>(entity, out var stats) &&
                stats.PrimaryDriveUid is { } cachedDrive &&
                Exists(cachedDrive) &&
                TryComp<FileSystemComponent>(cachedDrive, out fsComp))
            {
                driveUid = cachedDrive;
                return true;
            }

            if (TryComp<ModularComputerComponent>(entity, out var mod) && mod.InstalledParts.Count > 0)
            {
                foreach (var part in mod.InstalledParts)
                {
                    if (HasComp<ComputerHardDriveComponent>(part) && TryComp<FileSystemComponent>(part, out fsComp))
                    {
                        driveUid = part;
                        return true;
                    }
                }
            }

            if (_container.TryGetContainer(entity, ModularComputerComponent.ContainerHardware, out var hwContainer))
            {
                foreach (var part in hwContainer.ContainedEntities)
                {
                    if (HasComp<ComputerHardDriveComponent>(part) && TryComp<FileSystemComponent>(part, out fsComp))
                    {
                        driveUid = part;
                        return true;
                    }
                }
            }

            return false;
        }

        // Direct storage item (HDD, optical disk, standalone data drive).
        if (TryComp<FileSystemComponent>(entity, out fsComp))
        {
            driveUid = entity;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the very first valid Nanotransen executable (.nte) file on the drive in filesystem order.
    /// Computers always execute the first executable file on their hard drive at boot.
    /// </summary>
    public bool TryGetFirstExecutable(
        EntityUid driveUid,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(true)] out ProgramVirtualFile? execFile,
        FileSystemComponent? fs = null)
    {
        fullPath = null;
        execFile = null;

        if (!Resolve(driveUid, ref fs, false))
            return false;

        foreach (var (path, file) in fs.Files)
        {
            if (file is ProgramVirtualFile progFile &&
                progFile.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = path;
                execFile = progFile;
                return true;
            }
        }

        return false;
    }

    public bool TryGetFile<T>(EntityUid uid, string fullPath, [NotNullWhen(true)] out T? file, FileSystemComponent? comp = null) where T : VirtualFile
    {
        file = null;
        if (!Resolve(uid, ref comp))
            return false;

        var normPath = NormalizePath(fullPath);
        if (comp.Files.TryGetValue(normPath, out var raw) && raw is T typed)
        {
            file = typed;
            return true;
        }

        return false;
    }

    public IEnumerable<T> GetFilesByType<T>(EntityUid uid, FileSystemComponent? comp = null) where T : VirtualFile
    {
        if (!Resolve(uid, ref comp))
            yield break;

        foreach (var file in comp.Files.Values)
        {
            if (file is T typed)
                yield return typed;
        }
    }

    public List<string> GetDirectories(FileSystemComponent comp)
    {
        var dirs = new HashSet<string> { "/" };
        foreach (var file in comp.Files.Values)
        {
            var dir = NormalizeDirectory(file.Directory);
            var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var accum = "/";
            foreach (var p in parts)
            {
                accum += p + "/";
                dirs.Add(accum);
            }
        }
        return dirs.OrderBy(d => d).ToList();
    }

    public List<FileHeader> GetHeadersInDirectory(FileSystemComponent comp, string directory)
    {
        var normDir = NormalizeDirectory(directory);
        var list = new List<FileHeader>();
        foreach (var file in comp.Files.Values)
        {
            var fileDir = NormalizeDirectory(file.Directory);
            if (fileDir == normDir)
            {
                list.Add(new FileHeader(
                    file.FullPath,
                    file.Name,
                    file.Extension,
                    file.Size,
                    file.FileTypeTag,
                    file.IsEncrypted,
                    file.IsReadOnly,
                    file.LastModified
                ));
            }
        }
        return list;
    }

    public virtual bool TryWriteFile(EntityUid uid, VirtualFile file, FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        file.Directory = NormalizeDirectory(file.Directory);
        file.Name = (file.Name ?? string.Empty).Replace('/', '_').Replace('\\', '_').Replace("..", "_").Trim();
        file.Extension = (file.Extension ?? string.Empty).Replace('/', '_').Replace('\\', '_').Replace('.', '_').Trim();
        if (string.IsNullOrWhiteSpace(file.Name) || string.IsNullOrWhiteSpace(file.Extension))
            return false;

        if (file is RecipeBlueprintFile bp)
        {
            // Encrypted recipe files are a physical recipe-disk-only concept and cannot be stored on computer hard drives.
            if ((bp.IsEncrypted || !string.IsNullOrWhiteSpace(bp.EncryptionKeyHash)) &&
                HasComp<ComputerHardDriveComponent>(uid))
                return false;
        }

        file.Size = file.CalculateRealSize(_proto, _compFactory);

        var fullPath = NormalizePath(file.FullPath);
        if (comp.Files.TryGetValue(fullPath, out var existing))
        {
            if (existing.IsReadOnly)
                return false;

            // Never allow a text/data write to corrupt an existing executable program file.
            if (existing is ProgramVirtualFile && file is not ProgramVirtualFile)
                return false;
        }

        // Prevent non-program writes into /bin/*.nte.
        if (file is not ProgramVirtualFile &&
            file.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sizeDelta = file.Size - (existing?.Size ?? 0);
        if (comp.UsedStorageBytes + sizeDelta > comp.MaxStorageBytes)
            return false;

        file.LastModified = _timing.CurTime;
        comp.Files[fullPath] = file;
        RecalculateStorageUsage(comp);
        UpdateNetworkedHeaders(uid, comp);
        var ev = new FileSystemChangedEvent(fullPath);
        RaiseLocalEvent(uid, ref ev, broadcast: true);
        return true;
    }

    public virtual bool TryDeleteFile(EntityUid uid, string fullPath, FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        var normPath = NormalizePath(fullPath);
        if (!comp.Files.TryGetValue(normPath, out var file) || file.IsReadOnly)
            return false;

        if (comp.Files.Remove(normPath))
        {
            RecalculateStorageUsage(comp);
            UpdateNetworkedHeaders(uid, comp);
            var ev = new FileSystemChangedEvent(normPath);
            RaiseLocalEvent(uid, ref ev, broadcast: true);
            return true;
        }

        return false;
    }

    protected void UpdateNetworkedHeaders(EntityUid uid, FileSystemComponent comp)
    {
        comp.NetworkedHeaders = comp.Files.Values.Select(f => new FileHeader(
            f.FullPath,
            f.Name,
            f.Extension,
            f.Size,
            f.FileTypeTag,
            f.IsEncrypted,
            f.IsReadOnly,
            f.LastModified
        )).ToList();
        Dirty(uid, comp);
    }

    public void LogSystemActivity(
        EntityUid uid,
        string sessionType,
        string command,
        string? fileName = null,
        string? packageName = null,
        string? extra = null,
        FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        var timeStr = _timing.CurTime.ToString(@"hh\:mm\:ss");

        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(fileName))
            metaParts.Add($"[FILE: {fileName}]");
        if (!string.IsNullOrEmpty(packageName))
            metaParts.Add($"[PACKAGE: {packageName}]");

        var metaStr = metaParts.Count > 0 ? " " + string.Join(" ", metaParts) : "";
        var extraStr = !string.IsNullOrEmpty(extra) ? $" -> {extra}" : "";

        var logEntry = $"[{timeStr}] [{sessionType}]{metaStr} {command}{extraStr}";

        if (!TryGetFile<TextVirtualFile>(uid, "/sys/system.log", out var logFile, comp))
        {
            logFile = new TextVirtualFile
            {
                Name = "system",
                Extension = "log",
                Directory = "/sys/",
                Size = 128,
                TextContent = DefaultLogHeader,
                IsReadOnly = false
            };
            comp.LogHeader = DefaultLogHeader;
            comp.LogEntries = new Queue<string>(MaxLogEntries);
        }

        if (comp.LogEntries == null || comp.LogHeader == null)
        {
            var text = logFile.TextContent;
            var firstNl = text.IndexOf('\n');
            var secondNl = firstNl >= 0 ? text.IndexOf('\n', firstNl + 1) : -1;
            var queue = new Queue<string>(MaxLogEntries);

            if (secondNl >= 0)
            {
                comp.LogHeader = text.Substring(0, secondNl);
                var body = text.Substring(secondNl + 1);
                var existingLines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in existingLines)
                {
                    if (queue.Count >= MaxLogEntries)
                        queue.Dequeue();
                    queue.Enqueue(line);
                }
            }
            else
            {
                comp.LogHeader = DefaultLogHeader;
            }

            comp.LogEntries = queue;
        }

        while (comp.LogEntries.Count >= MaxLogEntries)
            comp.LogEntries.Dequeue();

        comp.LogEntries.Enqueue(logEntry);

        var sb = new StringBuilder(comp.LogHeader.Length + comp.LogEntries.Count * 96);
        sb.Append(comp.LogHeader);
        foreach (var entry in comp.LogEntries)
        {
            sb.Append('\n');
            sb.Append(entry);
        }

        logFile.TextContent = sb.ToString();
        TryWriteFile(uid, logFile, comp);
    }
}

[ByRefEvent]
public readonly record struct FileSystemChangedEvent(string Path);
