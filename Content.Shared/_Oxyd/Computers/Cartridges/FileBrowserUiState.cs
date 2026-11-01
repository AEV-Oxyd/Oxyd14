using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

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
        string? viewedFileName = null,
        string? viewedFileContent = null)
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
        ViewedFileName = viewedFileName;
        ViewedFileContent = viewedFileContent;
    }
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

    public FileBrowserItemDto(
        string name,
        string extension,
        string directory,
        int size,
        bool isReadOnly,
        bool isEncrypted,
        int? remainingUses)
    {
        Name = name;
        Extension = extension;
        Directory = directory;
        Size = size;
        IsReadOnly = isReadOnly;
        IsEncrypted = isEncrypted;
        RemainingUses = remainingUses;
    }
}
