using Content.Shared.Research.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent]
public sealed partial class DownloaderProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Ready";

    [DataField]
    public string CurrentCategory = "All";

    [DataField]
    public string? SelectedServerGuid;

    /// <summary>
    /// Last interacting actor used to keep access visibility consistent across background transfer progress ticks.
    /// </summary>
    [ViewVariables]
    public EntityUid? LastActor;
}

[Serializable, NetSerializable]
public sealed class DownloaderTransferDto
{
    public string Path { get; }
    public bool IsUpload { get; }
    public int TransferredBytes { get; }
    public int TotalBytes { get; }
    public int Percent { get; }

    public DownloaderTransferDto(string path, bool isUpload, int transferredBytes, int totalBytes)
    {
        Path = path;
        IsUpload = isUpload;
        TransferredBytes = transferredBytes;
        TotalBytes = totalBytes;
        Percent = totalBytes > 0 ? Math.Clamp(transferredBytes * 100 / totalBytes, 0, 100) : 100;
    }
}

[Serializable, NetSerializable]
public sealed class RepositoryItemDto
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public string RemotePath { get; }
    public int Size { get; }
    public bool IsDownloaded { get; }
    public string PrototypeId { get; }
    public List<ProtoId<LatheRecipePrototype>> Recipes { get; }
    public bool HasDownloadAccess { get; }
    public string RequiredAccessSummary { get; }

    public RepositoryItemDto(
        string id,
        string name,
        string category,
        string description,
        string remotePath,
        int size,
        bool isDownloaded,
        string prototypeId = "",
        List<ProtoId<LatheRecipePrototype>>? recipes = null,
        bool hasDownloadAccess = true,
        string requiredAccessSummary = "")
    {
        Id = id;
        Name = name;
        Category = category;
        Description = description;
        RemotePath = remotePath;
        Size = size;
        IsDownloaded = isDownloaded;
        PrototypeId = prototypeId;
        Recipes = recipes ?? new();
        HasDownloadAccess = hasDownloadAccess;
        RequiredAccessSummary = requiredAccessSummary;
    }
}

[Serializable, NetSerializable]
public sealed class LocalUploadCandidateDto
{
    public string Path { get; }
    public int Size { get; }

    public LocalUploadCandidateDto(string path, int size)
    {
        Path = path;
        Size = size;
    }
}

[Serializable, NetSerializable]
public sealed class DownloaderUiState : BoundUserInterfaceState
{
    public string ServerName { get; }
    public string ServerGuid { get; }
    public bool IsNetworkConnected { get; }
    public bool IsServerOnline { get; }
    public int DownloadBps { get; }
    public int UploadBps { get; }
    public string StatusMessage { get; }
    public string SelectedCategory { get; }
    public List<RepositoryItemDto> Items { get; }
    public List<LocalUploadCandidateDto> LocalUploadables { get; }
    public List<DownloaderTransferDto> ActiveTransfers { get; }

    public DownloaderUiState(
        string serverName,
        string serverGuid,
        bool isNetworkConnected,
        bool isServerOnline,
        int downloadBps,
        int uploadBps,
        string statusMessage,
        string selectedCategory,
        List<RepositoryItemDto> items,
        List<LocalUploadCandidateDto> localUploadables,
        List<DownloaderTransferDto>? activeTransfers = null)
    {
        ServerName = serverName;
        ServerGuid = serverGuid;
        IsNetworkConnected = isNetworkConnected;
        IsServerOnline = isServerOnline;
        DownloadBps = downloadBps;
        UploadBps = uploadBps;
        StatusMessage = statusMessage;
        SelectedCategory = selectedCategory;
        Items = items;
        LocalUploadables = localUploadables;
        ActiveTransfers = activeTransfers ?? new();
    }
}

[Serializable, NetSerializable]
public enum DownloaderAction : byte
{
    Refresh,
    SelectCategory,
    DownloadItem,
    UploadFile,
    CancelTransfer,
    CancelItem
}

[Serializable, NetSerializable]
public sealed class DownloaderActionMessage : BoundUserInterfaceMessage
{
    public DownloaderAction Action { get; }
    public string Parameter { get; }

    public DownloaderActionMessage(DownloaderAction action, string parameter = "")
    {
        Action = action;
        Parameter = parameter;
    }
}
