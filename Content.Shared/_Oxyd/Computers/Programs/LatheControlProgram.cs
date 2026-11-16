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
    public List<string> QueueRecipes = new();

    [DataField]
    public Dictionary<string, int> Materials = new();

    [DataField]
    public List<string> HostRecipes = new();

    [DataField]
    public string? InsertedDiskName;

    [DataField]
    public string StatusMessage = "Awaiting DeviceLink telemetry from Autolathe...";
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

    public LatheBlueprintEntryDto(
        string recipeId,
        string recipeName,
        string filePath,
        int? remainingUses,
        bool isDrmLocked,
        bool isEncrypted,
        bool fromDisk)
    {
        RecipeId = recipeId;
        RecipeName = recipeName;
        FilePath = filePath;
        RemainingUses = remainingUses;
        IsDrmLocked = isDrmLocked;
        IsEncrypted = isEncrypted;
        FromDisk = fromDisk;
    }
}

[Serializable, NetSerializable]
public sealed class LatheControlUiState : BoundUserInterfaceState
{
    public bool HasTelemetry { get; }
    public bool IsDeviceLinkEnabled { get; }
    public string LatheStatus { get; }
    public string CurrentPrinting { get; }
    public List<string> QueueRecipes { get; }
    public Dictionary<string, int> Materials { get; }
    public List<LatheBlueprintEntryDto> AvailableRecipes { get; }
    public string? InsertedDiskName { get; }
    public string StatusMessage { get; }

    public LatheControlUiState(
        bool hasTelemetry,
        bool isDeviceLinkEnabled,
        string latheStatus,
        string currentPrinting,
        List<string> queueRecipes,
        Dictionary<string, int> materials,
        List<LatheBlueprintEntryDto> availableRecipes,
        string? insertedDiskName,
        string statusMessage)
    {
        HasTelemetry = hasTelemetry;
        IsDeviceLinkEnabled = isDeviceLinkEnabled;
        LatheStatus = latheStatus;
        CurrentPrinting = currentPrinting;
        QueueRecipes = queueRecipes;
        Materials = materials;
        AvailableRecipes = availableRecipes;
        InsertedDiskName = insertedDiskName;
        StatusMessage = statusMessage;
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
    DecryptBlueprint
}

[Serializable, NetSerializable]
public sealed class LatheControlActionMessage : BoundUserInterfaceMessage
{
    public LatheControlAction Action { get; }
    public string RecipeId { get; }
    public string FilePath { get; }
    public int Amount { get; }

    public LatheControlActionMessage(
        LatheControlAction action,
        string recipeId = "",
        string filePath = "",
        int amount = 1)
    {
        Action = action;
        RecipeId = recipeId;
        FilePath = filePath;
        Amount = amount;
    }
}
