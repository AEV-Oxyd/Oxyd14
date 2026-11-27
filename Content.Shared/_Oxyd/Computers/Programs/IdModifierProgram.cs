using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated NTOS ID Card Modification program (/bin/id_console.nte).
/// Requires 2 ID Card Slot hardware modules (Slot #1 = Modifying / Auth ID, Slot #2 = Target ID).
/// Supports authorizing via Modifying ID card in Slot #1 or /keys/*.acc files.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IdModifierProgramComponent : Component
{
    [DataField]
    public string? SelectedAccFilePath;

    [DataField]
    public string StatusMessage = "Requires 2 ID Card Slot modules. Insert Modifying ID (Slot #1 or .acc) and Target ID (Slot #2).";
}

[Serializable, NetSerializable]
public sealed class IdModifierUiState : BoundUserInterfaceState
{
    public int SlotCount { get; }
    public bool HasRequiredModificationHardware { get; }
    public bool Slot1Occupied { get; }
    public string Slot1CardLabel { get; }
    public bool Slot2Occupied { get; }
    public string Slot2CardLabel { get; }
    public string PrivilegedOwner { get; }
    public List<string> PrivilegedTags { get; }
    public string SelectedAccFilePath { get; }
    public List<string> ExistingAccFiles { get; }
    public bool HasTargetCard { get; }
    public string TargetCardName { get; }
    public string TargetFullName { get; }
    public string TargetJobTitle { get; }
    public string TargetJobPrototype { get; }
    public List<string> TargetAccessTags { get; }
    public string StatusMessage { get; }

    public IdModifierUiState(
        int slotCount,
        bool hasRequiredModificationHardware,
        bool slot1Occupied,
        string slot1CardLabel,
        bool slot2Occupied,
        string slot2CardLabel,
        string privilegedOwner,
        List<string> privilegedTags,
        string selectedAccFilePath,
        List<string> existingAccFiles,
        bool hasTargetCard,
        string targetCardName,
        string targetFullName,
        string targetJobTitle,
        string targetJobPrototype,
        List<string> targetAccessTags,
        string statusMessage)
    {
        SlotCount = slotCount;
        HasRequiredModificationHardware = hasRequiredModificationHardware;
        Slot1Occupied = slot1Occupied;
        Slot1CardLabel = slot1CardLabel;
        Slot2Occupied = slot2Occupied;
        Slot2CardLabel = slot2CardLabel;
        PrivilegedOwner = privilegedOwner;
        PrivilegedTags = privilegedTags;
        SelectedAccFilePath = selectedAccFilePath;
        ExistingAccFiles = existingAccFiles;
        HasTargetCard = hasTargetCard;
        TargetCardName = targetCardName;
        TargetFullName = targetFullName;
        TargetJobTitle = targetJobTitle;
        TargetJobPrototype = targetJobPrototype;
        TargetAccessTags = targetAccessTags;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum IdModifierAction : byte
{
    Refresh,
    TogglePrivilegedSlot,
    ToggleTargetSlot,
    SwapSlots,
    SelectAccFile,
    WriteTargetIdentity,
    ToggleTargetAccessTag,
    ApplyAccFileToTarget,
    GrantAllAllowedAccess,
    RevokeAllAllowedAccess
}

[Serializable, NetSerializable]
public sealed class IdModifierActionMessage : BoundUserInterfaceMessage
{
    public IdModifierAction Action { get; }
    public string Argument { get; }
    public string SecondaryArgument { get; }
    public string TertiaryArgument { get; }

    public IdModifierActionMessage(
        IdModifierAction action,
        string argument = "",
        string secondaryArgument = "",
        string tertiaryArgument = "")
    {
        Action = action;
        Argument = argument;
        SecondaryArgument = secondaryArgument;
        TertiaryArgument = tertiaryArgument;
    }
}
