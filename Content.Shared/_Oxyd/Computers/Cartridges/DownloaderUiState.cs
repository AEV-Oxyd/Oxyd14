using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[Serializable, NetSerializable]
public sealed class DownloaderUiState : BoundUserInterfaceState
{
    public string ServerName { get; }
    public string ServerGuid { get; }
    public bool IsServerOnline { get; }
    public int BandwidthBps { get; }
    public string StatusMessage { get; }
    public string SelectedCategory { get; }
    public List<RepositoryItemDto> Items { get; }

    public DownloaderUiState(
        string serverName,
        string serverGuid,
        bool isServerOnline,
        int bandwidthBps,
        string statusMessage,
        string selectedCategory,
        List<RepositoryItemDto> items)
    {
        ServerName = serverName;
        ServerGuid = serverGuid;
        IsServerOnline = isServerOnline;
        BandwidthBps = bandwidthBps;
        StatusMessage = statusMessage;
        SelectedCategory = selectedCategory;
        Items = items;
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
    public bool IsInstalled { get; }
    public string PrototypeId { get; }
    public List<ProtoId<LatheRecipePrototype>> Recipes { get; }

    public RepositoryItemDto(
        string id,
        string name,
        string category,
        string description,
        string remotePath,
        int size,
        bool isDownloaded,
        bool isInstalled,
        string prototypeId = "",
        List<ProtoId<LatheRecipePrototype>>? recipes = null)
    {
        Id = id;
        Name = name;
        Category = category;
        Description = description;
        RemotePath = remotePath;
        Size = size;
        IsDownloaded = isDownloaded;
        IsInstalled = isInstalled;
        PrototypeId = prototypeId;
        Recipes = recipes ?? new();
    }
}
