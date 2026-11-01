using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Shared._Oxyd.Computers.FileSystem;

public class SharedFileSystemSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FileSystemComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnAfterState(EntityUid uid, FileSystemComponent comp, ref AfterAutoHandleStateEvent args)
    {
        RecalculateStorageUsage(comp);
    }

    public void RecalculateStorageUsage(FileSystemComponent comp)
    {
        comp.UsedStorageBytes = comp.Files.Values.Sum(f => f.Size);
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
}
