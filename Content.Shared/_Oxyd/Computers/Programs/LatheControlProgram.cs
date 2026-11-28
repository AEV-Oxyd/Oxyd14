using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent]
public sealed partial class LatheControlProgramComponent : Component
{
    [DataField]
    public bool HasTelemetry;

    [DataField]
    public string LatheStatus = "Offline";

    [DataField]
    public string CurrentPrinting = string.Empty;

    [DataField]
    public TimeSpan? ProductionStartTime;

    [DataField]
    public TimeSpan? ProductionLength;

    [DataField]
    public float MaterialUseMultiplier = 1f;

    [DataField]
    public List<string> QueueRecipes = new();

    [DataField]
    public List<LatheQueueEntryDto> QueueEntries = new();

    [DataField]
    public Dictionary<string, int> Materials = new();

    [DataField]
    public List<string> HostRecipes = new();

    [DataField]
    public string? InsertedDiskName;

    [ViewVariables]
    public EntityUid? InsertedDiskUid;

    [DataField]
    public string SelectedRecipeId = string.Empty;

    [DataField]
    public string SelectedRecipeFilePath = string.Empty;

    [DataField]
    public bool SelectedRecipeFromDisk;

    [DataField]
    public string StatusMessage = "Awaiting DeviceLink telemetry from Autolathe...";
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class LatheQueueEntryDto
{
    [DataField]
    public string RecipeId = string.Empty;

    [DataField]
    public int Count = 1;

    public LatheQueueEntryDto()
    {
    }

    public LatheQueueEntryDto(string recipeId, int count)
    {
        RecipeId = recipeId;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class LatheBlueprintEntryDto
{
    public string RecipeId { get; }
    public string RecipeName { get; }
    public string FilePath { get; }
    public int? RemainingUses { get; }
    public bool IsDrmLocked { get; }
    public bool IsEncrypted { get; }
    public bool FromDisk { get; }
    public int FileSizeBytes { get; }
    public int PointCost { get; }

    public LatheBlueprintEntryDto(
        string recipeId,
        string recipeName,
        string filePath,
        int? remainingUses,
        bool isDrmLocked,
        bool isEncrypted,
        bool fromDisk,
        int fileSizeBytes = 0,
        int pointCost = 1)
    {
        RecipeId = recipeId;
        RecipeName = recipeName;
        FilePath = filePath;
        RemainingUses = remainingUses;
        IsDrmLocked = isDrmLocked;
        IsEncrypted = isEncrypted;
        FromDisk = fromDisk;
        FileSizeBytes = fileSizeBytes;
        PointCost = Math.Max(1, pointCost);
    }
}

[Serializable, NetSerializable]
public sealed class LatheControlUiState : BoundUserInterfaceState
{
    public bool HasTelemetry { get; }
    public bool IsDeviceLinkEnabled { get; }
    public string LatheStatus { get; }
    public string CurrentPrinting { get; }
    public TimeSpan? ProductionStartTime { get; }
    public TimeSpan? ProductionLength { get; }
    public float MaterialUseMultiplier { get; }
    public List<string> QueueRecipes { get; }
    public List<LatheQueueEntryDto> QueueEntries { get; }
    public Dictionary<string, int> Materials { get; }
    public List<LatheBlueprintEntryDto> AvailableRecipes { get; }
    public string? InsertedDiskName { get; }
    public string StatusMessage { get; }
    public int StorageUsedBytes { get; }
    public int StorageTotalBytes { get; }
    public string SelectedRecipeId { get; }
    public string SelectedRecipeFilePath { get; }
    public bool SelectedRecipeFromDisk { get; }

    public LatheControlUiState(
        bool hasTelemetry,
        bool isDeviceLinkEnabled,
        string latheStatus,
        string currentPrinting,
        List<string> queueRecipes,
        Dictionary<string, int> materials,
        List<LatheBlueprintEntryDto> availableRecipes,
        string? insertedDiskName,
        string statusMessage,
        TimeSpan? productionStartTime = null,
        TimeSpan? productionLength = null,
        float materialUseMultiplier = 1f,
        List<LatheQueueEntryDto>? queueEntries = null,
        int storageUsedBytes = 0,
        int storageTotalBytes = 0,
        string selectedRecipeId = "",
        string selectedRecipeFilePath = "",
        bool selectedRecipeFromDisk = false)
    {
        HasTelemetry = hasTelemetry;
        IsDeviceLinkEnabled = isDeviceLinkEnabled;
        LatheStatus = latheStatus;
        CurrentPrinting = currentPrinting;
        ProductionStartTime = productionStartTime;
        ProductionLength = productionLength;
        MaterialUseMultiplier = materialUseMultiplier;
        QueueRecipes = queueRecipes;
        QueueEntries = queueEntries ?? new List<LatheQueueEntryDto>();
        Materials = materials;
        AvailableRecipes = availableRecipes;
        InsertedDiskName = insertedDiskName;
        StatusMessage = statusMessage;
        StorageUsedBytes = storageUsedBytes;
        StorageTotalBytes = storageTotalBytes;
        SelectedRecipeId = selectedRecipeId;
        SelectedRecipeFilePath = selectedRecipeFilePath;
        SelectedRecipeFromDisk = selectedRecipeFromDisk;
    }
}

[Serializable, NetSerializable]
public enum LatheControlAction : byte
{
    Refresh,
    PrintRecipe,
    CancelAll,
    EjectMaterial,
    ImportDiskBlueprints,
    DecryptBlueprint,
    EjectDisk,
    SelectRecipe
}

[Serializable, NetSerializable]
public sealed class LatheControlActionMessage : BoundUserInterfaceMessage
{
    public LatheControlAction Action { get; }
    public string RecipeId { get; }
    public string FilePath { get; }
    public int Amount { get; }
    public bool FromDisk { get; }

    public LatheControlActionMessage(
        LatheControlAction action,
        string recipeId = "",
        string filePath = "",
        int amount = 1,
        bool fromDisk = false)
    {
        Action = action;
        RecipeId = recipeId;
        FilePath = filePath;
        Amount = amount;
        FromDisk = fromDisk;
    }
}
