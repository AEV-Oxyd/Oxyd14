using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed record LatheBlueprintFileDto(
    string FileName,
    string FullPath,
    int? RemainingUses,
    bool DrmLocked,
    bool IsEncrypted,
    List<ProtoId<LatheRecipePrototype>> Recipes
);

[Serializable, NetSerializable]
public sealed class LatheUpdateState : BoundUserInterfaceState
{
    public List<ProtoId<LatheRecipePrototype>> Recipes;

    public LatheRecipeBatch[] Queue;

    public ProtoId<LatheRecipePrototype>? CurrentlyProducing;

    public TimeSpan? ProductionStartTime;
    public TimeSpan? ProductionLength;

    public string diskName = string.Empty;
    public int uses = 0;

    public List<LatheBlueprintFileDto> BlueprintFiles = new();
    public string? SelectedBlueprintFile;

    public LatheUpdateState(
        List<ProtoId<LatheRecipePrototype>> recipes,
        LatheRecipeBatch[] queue,
        ProtoId<LatheRecipePrototype>? currentlyProducing = null,
        TimeSpan? productionStartTime = null,
        TimeSpan? productionLength = null,
        List<LatheBlueprintFileDto>? blueprintFiles = null,
        string? selectedBlueprintFile = null)
    {
        Recipes = recipes;
        Queue = queue;
        CurrentlyProducing = currentlyProducing;
        ProductionStartTime = productionStartTime;
        ProductionLength = productionLength;
        BlueprintFiles = blueprintFiles ?? new();
        SelectedBlueprintFile = selectedBlueprintFile;
    }
}

/// <summary>
///     Sent to the server to sync material storage and the recipe queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSyncRequestMessage : BoundUserInterfaceMessage
{

}

/// <summary>
///     Sent to the server when a client queues a new recipe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheQueueRecipeMessage : BoundUserInterfaceMessage
{
    public readonly string ID;
    public readonly int Quantity;
    public LatheQueueRecipeMessage(string id, int quantity)
    {
        ID = id;
        Quantity = quantity;
    }
}

/// <summary>
///     Sent to the server to remove a batch from the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheDeleteRequestMessage(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

/// <summary>
///     Sent to the server to move the position of a batch in the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheMoveRequestMessage(int index, int change) : BoundUserInterfaceMessage
{
    public int Index = index;
    public int Change = change;
}

/// <summary>
///     Sent to the server to stop producing the current item.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheAbortFabricationMessage() : BoundUserInterfaceMessage
{
}

/// <summary>
///     Sent to the server when a client selects a blueprint file in the lathe file browser.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSelectBlueprintFileMessage : BoundUserInterfaceMessage
{
    public readonly string? FilePath;

    public LatheSelectBlueprintFileMessage(string? filePath)
    {
        FilePath = filePath;
    }
}

[NetSerializable, Serializable]
public enum LatheUiKey
{
    Key,
}
