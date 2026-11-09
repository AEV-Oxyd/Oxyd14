using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Content.Shared._Oxyd.Computers.FileSystem;

public class SharedFileSystemSystem : EntitySystem
{
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;

    private const string DefaultLogHeader = "=== NTOS 1.4™ SYSTEM AUDIT LOG ===\n----------------------------------------";
    private const int MaxLogEntries = 148;

    private readonly Dictionary<EntityUid, (string Header, Queue<string> Entries)> _logBuffers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FileSystemComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<FileSystemComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, FileSystemComponent comp, ref ComponentShutdown args)
    {
        _logBuffers.Remove(uid);
    }

    private void OnAfterState(EntityUid uid, FileSystemComponent comp, ref AfterAutoHandleStateEvent args)
    {
        comp.UsedStorageBytes = comp.NetworkedHeaders.Sum(h => h.Size);
    }

    public void RecalculateStorageUsage(FileSystemComponent comp)
    {
        if (comp.Files.Count > 0)
            comp.UsedStorageBytes = comp.Files.Values.Sum(f => f.Size);
        else
            comp.UsedStorageBytes = comp.NetworkedHeaders.Sum(h => h.Size);
    }

    public bool TryGetFile<T>(EntityUid uid, string fullPath, [NotNullWhen(true)] out T? file, FileSystemComponent? comp = null) where T : VirtualFile
    {
        file = null;
        if (!Resolve(uid, ref comp))
            return false;

        if (comp.Files.TryGetValue(fullPath, out var raw) && raw is T typed)
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
            var dir = file.Directory;
            if (!dir.StartsWith('/')) dir = "/" + dir;
            if (!dir.EndsWith('/')) dir += "/";

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
        if (!directory.StartsWith('/')) directory = "/" + directory;
        if (!directory.EndsWith('/')) directory += "/";

        var list = new List<FileHeader>();
        foreach (var file in comp.Files.Values)
        {
            var fileDir = file.Directory;
            if (!fileDir.StartsWith('/')) fileDir = "/" + fileDir;
            if (!fileDir.EndsWith('/')) fileDir += "/";

            if (fileDir == directory)
            {
                list.Add(new FileHeader(
                    file.FullPath,
                    file.Name,
                    file.Extension,
                    file.Size,
                    file.GetType().Name,
                    file.IsEncrypted,
                    file.IsReadOnly
                ));
            }
        }
        return list;
    }

    public virtual bool TryWriteFile(EntityUid uid, VirtualFile file, FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        var fullPath = file.FullPath;
        if (comp.Files.TryGetValue(fullPath, out var existing) && existing.IsReadOnly)
            return false;

        var sizeDelta = file.Size - (existing?.Size ?? 0);
        if (comp.UsedStorageBytes + sizeDelta > comp.MaxStorageBytes)
            return false;

        comp.Files[fullPath] = file;
        RecalculateStorageUsage(comp);
        UpdateNetworkedHeaders(uid, comp);
        var ev = new FileSystemChangedEvent(fullPath);
        RaiseLocalEvent(uid, ref ev);
        return true;
    }

    public virtual bool TryDeleteFile(EntityUid uid, string fullPath, FileSystemComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        if (!comp.Files.TryGetValue(fullPath, out var file) || file.IsReadOnly)
            return false;

        if (comp.Files.Remove(fullPath))
        {
            RecalculateStorageUsage(comp);
            UpdateNetworkedHeaders(uid, comp);
            var ev = new FileSystemChangedEvent(fullPath);
            RaiseLocalEvent(uid, ref ev);
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
            f.GetType().Name,
            f.IsEncrypted,
            f.IsReadOnly
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
            _logBuffers[uid] = (DefaultLogHeader, new Queue<string>(MaxLogEntries));
        }

        if (!_logBuffers.TryGetValue(uid, out var buffer))
        {
            var text = logFile.TextContent;
            var firstNl = text.IndexOf('\n');
            var secondNl = firstNl >= 0 ? text.IndexOf('\n', firstNl + 1) : -1;
            string header;
            var queue = new Queue<string>(MaxLogEntries);

            if (secondNl >= 0)
            {
                header = text.Substring(0, secondNl);
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
                header = DefaultLogHeader;
            }

            buffer = (header, queue);
            _logBuffers[uid] = buffer;
        }

        while (buffer.Entries.Count >= MaxLogEntries)
        {
            buffer.Entries.Dequeue();
        }
        buffer.Entries.Enqueue(logEntry);

        var sb = new StringBuilder(buffer.Header.Length + buffer.Entries.Count * 96);
        sb.Append(buffer.Header);
        foreach (var entry in buffer.Entries)
        {
            sb.Append('\n');
            sb.Append(entry);
        }

        logFile.TextContent = sb.ToString();
        logFile.Size = Math.Max(64, logFile.TextContent.Length);
        TryWriteFile(uid, logFile, comp);
    }
}

[ByRefEvent]
public readonly record struct FileSystemChangedEvent(string Path);
