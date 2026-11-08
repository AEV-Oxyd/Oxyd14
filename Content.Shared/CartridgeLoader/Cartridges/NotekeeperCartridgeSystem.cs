using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared._Oxyd.Computers.FileSystem;

namespace Content.Shared.CartridgeLoader.Cartridges;

public sealed partial class NotekeeperCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoaderSystem = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedPdaSystem _pdaSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeActivatedEvent>(OnCartridgeActivated);
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeRelayedEvent<FileSystemChangedEvent>>(OnRelayedFileSystemChanged);
    }

    private void OnCartridgeActivated(EntityUid uid, NotekeeperCartridgeComponent component, ref CartridgeActivatedEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void OnRelayedFileSystemChanged(EntityUid uid, NotekeeperCartridgeComponent comp, ref CartridgeRelayedEvent<FileSystemChangedEvent> args)
    {
        UpdateUiState(uid, args.Loader.Owner, comp);
    }

    private void OnUiReady(EntityUid uid, NotekeeperCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        var loaderUid = args.Loader;

        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp))
        {
            if (!_fileSystem.TryGetFile<TextVirtualFile>(loaderUid, "/notes/shift_notes.txt", out _, fsComp) &&
                !_fileSystem.TryGetFile<TextVirtualFile>(loaderUid, "/notes/notes.txt", out _, fsComp))
            {
                TrySaveTextFile(loaderUid, "/notes/shift_notes.txt", "Station operations in progress.");
            }
        }

        UpdateUiState(uid, loaderUid, component);
        _pdaSystem.UpdatePdaUi(loaderUid);
    }

    private void OnUiMessage(EntityUid uid, NotekeeperCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not NotekeeperUiMessageEvent message)
            return;

        var loaderUid = GetEntity(args.LoaderUid);

        switch (message.Action)
        {
            case NotekeeperUiAction.OpenFile:
                if (!string.IsNullOrWhiteSpace(message.Path))
                {
                    component.CurrentFilePath = message.Path;
                    Dirty(uid, component);
                }
                break;

            case NotekeeperUiAction.SaveFile:
            case NotekeeperUiAction.LiveDraftUpdate:
                var savePath = string.IsNullOrWhiteSpace(message.Path) ? component.CurrentFilePath : message.Path;
                if (TrySaveTextFile(loaderUid, savePath, message.Content))
                {
                    component.CurrentFilePath = savePath;
                    Dirty(uid, component);
                    if (message.Action == NotekeeperUiAction.SaveFile)
                    {
                        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                            $"{ToPrettyString(args.Actor)} saved text file '{savePath}' in Notekeeper on: {ToPrettyString(uid)}");
                    }
                }
                break;

            case NotekeeperUiAction.CreateFile:
                if (TryCreateTextFile(loaderUid, message.Path, message.Content, out var newPath))
                {
                    component.CurrentFilePath = newPath;
                    Dirty(uid, component);
                    _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                        $"{ToPrettyString(args.Actor)} created text file '{newPath}' in Notekeeper on: {ToPrettyString(uid)}");
                }
                break;

            case NotekeeperUiAction.DeleteFile:
                var delPath = string.IsNullOrWhiteSpace(message.Path) ? component.CurrentFilePath : message.Path;
                if (delPath != "/notes/shift_notes.txt" && TryDeleteTextFile(loaderUid, delPath))
                {
                    _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                        $"{ToPrettyString(args.Actor)} deleted text file '{delPath}' in Notekeeper on: {ToPrettyString(uid)}");
                    component.CurrentFilePath = "/notes/shift_notes.txt";
                    Dirty(uid, component);
                }
                break;

            case NotekeeperUiAction.Add:
                var appendPath = component.CurrentFilePath;
                TryReadTextFile(loaderUid, appendPath, out var existingText, out var isReadOnly);
                if (!isReadOnly)
                {
                    var updated = string.IsNullOrEmpty(existingText)
                        ? message.Path
                        : $"{existingText}\n{message.Path}";
                    TrySaveTextFile(loaderUid, appendPath, updated);
                    _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                        $"{ToPrettyString(args.Actor)} appended note to '{appendPath}' on: {ToPrettyString(uid)}");
                }
                break;

            case NotekeeperUiAction.Remove:
                var rmPath = component.CurrentFilePath;
                if (TryReadTextFile(loaderUid, rmPath, out var rmText, out var rmRo) && !rmRo)
                {
                    var lines = rmText.Split('\n').ToList();
                    lines.Remove(message.Path);
                    TrySaveTextFile(loaderUid, rmPath, string.Join("\n", lines));
                    _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
                        $"{ToPrettyString(args.Actor)} removed note line from '{rmPath}' on: {ToPrettyString(uid)}");
                }
                break;
        }

        UpdateUiState(uid, loaderUid, component);
        _pdaSystem.UpdatePdaUi(loaderUid);
    }

    public void UpdateUiState(EntityUid uid, EntityUid loaderUid, NotekeeperCartridgeComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var availableFiles = GetAllTextFiles(loaderUid);
        if (availableFiles.Count == 0)
        {
            TrySaveTextFile(loaderUid, "/notes/shift_notes.txt", "Station operations in progress.");
            availableFiles.Add("/notes/shift_notes.txt");
        }

        if (string.IsNullOrWhiteSpace(component.CurrentFilePath) || !availableFiles.Contains(component.CurrentFilePath))
        {
            component.CurrentFilePath = availableFiles[0];
            Dirty(uid, component);
        }

        TryReadTextFile(loaderUid, component.CurrentFilePath, out var content, out var isReadOnly);
        content ??= "";

        component.Notes = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        var state = new NotekeeperUiState(
            component.Notes,
            component.CurrentFilePath,
            GetFileName(component.CurrentFilePath),
            content,
            availableFiles,
            isReadOnly
        );

        _cartridgeLoaderSystem.UpdateCartridgeUiState(loaderUid, state);
    }

    private List<string> GetAllTextFiles(EntityUid loaderUid)
    {
        var result = new List<string>();
        if (TryComp<FileSystemComponent>(loaderUid, out var fsComp))
        {
            foreach (var file in _fileSystem.GetFilesByType<TextVirtualFile>(loaderUid, fsComp))
            {
                if (!result.Contains(file.FullPath))
                    result.Add(file.FullPath);
            }
        }

        if (TryGetDisk(loaderUid, out var diskUid, out var diskFs, out _))
        {
            foreach (var file in _fileSystem.GetFilesByType<TextVirtualFile>(diskUid, diskFs))
            {
                var diskPath = file.FullPath.StartsWith("/disk/") ? file.FullPath : $"/disk{file.FullPath}";
                if (!result.Contains(diskPath))
                    result.Add(diskPath);
            }
        }

        result.Sort((a, b) =>
        {
            if (a == "/notes/shift_notes.txt") return -1;
            if (b == "/notes/shift_notes.txt") return 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private bool TryReadTextFile(EntityUid loaderUid, string path, [NotNullWhen(true)] out string? content, out bool isReadOnly)
    {
        content = null;
        isReadOnly = false;

        if (path.StartsWith("/disk/") && TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
        {
            var searchPath = path.Substring("/disk".Length);
            if (!searchPath.StartsWith("/")) searchPath = "/" + searchPath;

            if (_fileSystem.TryGetFile<TextVirtualFile>(dUid, searchPath, out var dFile, dFs) ||
                _fileSystem.TryGetFile<TextVirtualFile>(dUid, path, out dFile, dFs))
            {
                content = dFile.TextContent;
                isReadOnly = dFile.IsReadOnly;
                return true;
            }
        }
        else if (TryComp<FileSystemComponent>(loaderUid, out var localFs))
        {
            if (_fileSystem.TryGetFile<TextVirtualFile>(loaderUid, path, out var lFile, localFs))
            {
                content = lFile.TextContent;
                isReadOnly = lFile.IsReadOnly;
                return true;
            }
        }

        return false;
    }

    private bool TrySaveTextFile(EntityUid loaderUid, string path, string content)
    {
        var dynamicSize = Math.Max(32, content.Length + 32);

        if (path.StartsWith("/disk/") && TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
        {
            var searchPath = path.Substring("/disk".Length);
            if (!searchPath.StartsWith("/")) searchPath = "/" + searchPath;

            var dir = searchPath.Substring(0, searchPath.LastIndexOf('/') + 1);
            if (string.IsNullOrEmpty(dir)) dir = "/";
            var filename = searchPath.Substring(searchPath.LastIndexOf('/') + 1);
            var nameWithoutExt = filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? filename.Substring(0, filename.Length - 4)
                : filename;

            var updatedFile = new TextVirtualFile
            {
                Name = nameWithoutExt,
                Extension = "txt",
                Directory = dir,
                Size = dynamicSize,
                TextContent = content,
                IsReadOnly = false
            };
            return _fileSystem.TryWriteFile(dUid, updatedFile, dFs);
        }
        else if (TryComp<FileSystemComponent>(loaderUid, out var localFs))
        {
            var dir = path.Substring(0, path.LastIndexOf('/') + 1);
            if (string.IsNullOrEmpty(dir)) dir = "/notes/";
            var filename = path.Substring(path.LastIndexOf('/') + 1);
            var nameWithoutExt = filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? filename.Substring(0, filename.Length - 4)
                : filename;

            var updatedFile = new TextVirtualFile
            {
                Name = nameWithoutExt,
                Extension = "txt",
                Directory = dir,
                Size = dynamicSize,
                TextContent = content,
                IsReadOnly = false
            };
            return _fileSystem.TryWriteFile(loaderUid, updatedFile, localFs);
        }

        return false;
    }

    private bool TryCreateTextFile(EntityUid loaderUid, string rawName, string initialContent, out string createdPath)
    {
        createdPath = "";
        var cleanName = GetFileNameWithoutExtension(rawName.Trim());
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = "untitled";

        cleanName = string.Concat(cleanName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = "untitled";

        var dir = "/notes/";
        var targetPath = $"{dir}{cleanName}.txt";

        if (TrySaveTextFile(loaderUid, targetPath, initialContent))
        {
            createdPath = targetPath;
            return true;
        }

        return false;
    }

    private bool TryDeleteTextFile(EntityUid loaderUid, string path)
    {
        if (path.StartsWith("/disk/") && TryGetDisk(loaderUid, out var dUid, out var dFs, out _))
        {
            var searchPath = path.Substring("/disk".Length);
            if (!searchPath.StartsWith("/")) searchPath = "/" + searchPath;
            return _fileSystem.TryDeleteFile(dUid, searchPath, dFs);
        }
        else if (TryComp<FileSystemComponent>(loaderUid, out var localFs))
        {
            return _fileSystem.TryDeleteFile(loaderUid, path, localFs);
        }

        return false;
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

    private static string GetFileName(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
    }

    private static string GetFileNameWithoutExtension(string path)
    {
        var name = GetFileName(path);
        var lastDot = name.LastIndexOf('.');
        return lastDot > 0 ? name.Substring(0, lastDot) : name;
    }
}
