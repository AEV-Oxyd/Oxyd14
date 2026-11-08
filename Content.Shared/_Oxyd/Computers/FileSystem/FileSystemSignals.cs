using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.FileSystem;

[ByRefEvent]
public record struct RequestOpenFilePickerEvent(EntityUid TargetEntity, string InitialPath = "/", string[]? AllowedExtensions = null, bool AllowFolderSelection = true);

[ByRefEvent]
public record struct FileSelectedSignal(string Path, string FileName, string Extension);

[ByRefEvent]
public record struct FolderSelectedSignal(string DirectoryPath);

[Serializable, NetSerializable]
public sealed class FilePickerSelectMessage : BoundUserInterfaceMessage
{
    public string SelectedPath { get; set; } = "/";
    public bool IsDirectory { get; set; }

    public FilePickerSelectMessage(string selectedPath, bool isDirectory)
    {
        SelectedPath = selectedPath;
        IsDirectory = isDirectory;
    }
}

[Serializable, NetSerializable]
public sealed class FilePickerState : BoundUserInterfaceState
{
    public List<FileHeader> Files { get; set; } = new();
    public List<string> Directories { get; set; } = new();
    public string CurrentDirectory { get; set; } = "/";

    public FilePickerState(List<FileHeader> files, List<string> directories, string currentDirectory)
    {
        Files = files;
        Directories = directories;
        CurrentDirectory = currentDirectory;
    }
}
