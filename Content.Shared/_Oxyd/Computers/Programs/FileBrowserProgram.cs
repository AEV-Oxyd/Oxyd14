using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent]
public sealed partial class FileBrowserProgramComponent : Component
{
    [DataField]
    public string CurrentDirectory = "/";

    [DataField]
    public string StatusMessage = "Ready";

    [DataField]
    public string? ViewedFilePath;

    [DataField]
    public bool ViewedFileIsOnDisk;
}

[Serializable, NetSerializable]
public sealed class FileBrowserItemDto
{
    public string Name { get; }
    public string Extension { get; }
    public string Directory { get; }
    public int Size { get; }
    public bool IsReadOnly { get; }
    public bool IsEncrypted { get; }
    public int? RemainingUses { get; }
    public bool IsOnDisk { get; }
    public TimeSpan LastModified { get; }

    public FileBrowserItemDto(
        string name,
        string extension,
        string directory,
        int size,
        bool isReadOnly,
        bool isEncrypted,
        int? remainingUses,
        bool isOnDisk = false,
        TimeSpan lastModified = default)
    {
        Name = name;
        Extension = extension;
        Directory = directory;
        Size = size;
        IsReadOnly = isReadOnly;
        IsEncrypted = isEncrypted;
        RemainingUses = remainingUses;
        IsOnDisk = isOnDisk;
        LastModified = lastModified;
    }
}

[Serializable, NetSerializable]
public sealed class FileBrowserUiState : BoundUserInterfaceState
{
    public string CurrentDirectory { get; }
    public List<FileBrowserItemDto> Files { get; }
    public string DeviceGuid { get; }
    public float CpuUsagePercent { get; }
    public int RamUsedMb { get; }
    public int RamTotalMb { get; }
    public int StorageUsedBytes { get; }
    public int StorageTotalBytes { get; }
    public int BandwidthBps { get; }
    public bool IsNetworkSaturated { get; }
    public string StatusMessage { get; }
    public string? ViewedFileName { get; }
    public string? ViewedFileContent { get; }
    public bool ViewedFileIsOnDisk { get; }
    public bool HasHardDrive { get; }
    public bool HasDisk { get; }
    public string? DiskName { get; }
    public int? DiskUsedBytes { get; }
    public int? DiskTotalBytes { get; }
    public TimeSpan? ViewedFileLastModified { get; }

    public FileBrowserUiState(
        string currentDirectory,
        List<FileBrowserItemDto> files,
        string deviceGuid,
        float cpuUsagePercent,
        int ramUsedMb,
        int ramTotalMb,
        int storageUsedBytes,
        int storageTotalBytes,
        int bandwidthBps,
        bool isNetworkSaturated,
        string statusMessage,
        bool hasHardDrive = true,
        string? viewedFileName = null,
        string? viewedFileContent = null,
        bool viewedFileIsOnDisk = false,
        bool hasDisk = false,
        string? diskName = null,
        int? diskUsedBytes = null,
        int? diskTotalBytes = null,
        TimeSpan? viewedFileLastModified = null)
    {
        CurrentDirectory = currentDirectory;
        Files = files;
        DeviceGuid = deviceGuid;
        CpuUsagePercent = cpuUsagePercent;
        RamUsedMb = ramUsedMb;
        RamTotalMb = ramTotalMb;
        StorageUsedBytes = storageUsedBytes;
        StorageTotalBytes = storageTotalBytes;
        BandwidthBps = bandwidthBps;
        IsNetworkSaturated = isNetworkSaturated;
        StatusMessage = statusMessage;
        HasHardDrive = hasHardDrive;
        ViewedFileName = viewedFileName;
        ViewedFileContent = viewedFileContent;
        ViewedFileIsOnDisk = viewedFileIsOnDisk;
        HasDisk = hasDisk;
        DiskName = diskName;
        DiskUsedBytes = diskUsedBytes;
        DiskTotalBytes = diskTotalBytes;
        ViewedFileLastModified = viewedFileLastModified;
    }
}

[Serializable, NetSerializable]
public enum FileBrowserAction : byte
{
    ChangeDirectory,
    ViewFile,
    CloseView,
    DeleteFile,
    RunProgram,
    CopyToDisk,
    CopyToInternal,
    CreateFile,
    EjectDisk
}

[Serializable, NetSerializable]
public sealed class FileBrowserActionMessage : BoundUserInterfaceMessage
{
    public FileBrowserAction Action { get; }
    public string Path { get; }

    public FileBrowserActionMessage(FileBrowserAction action, string path = "")
    {
        Action = action;
        Path = path;
    }
}
