using Content.Shared.Access;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated NTOS Access Encoder program (/bin/access_encoder.nte).
/// Reads credentials from an ID card inserted into an ID Card Slot module (or host PDA ID slot)
/// and writes / deletes .acc credential files in /keys/. Does not modify ID cards.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AccessEncoderProgramComponent : Component
{
    public const string DefaultDirectory = "/keys/";

    [DataField]
    public string? LastCardName;

    [DataField]
    public string? LastCardJobTitle;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> LastCardAccessTags = new();

    [DataField]
    public string? SelectedFilePath;

    [DataField]
    public string StatusMessage = "Insert an ID card into an ID Card Slot module (or PDA slot) to encode .acc access files.";
}

[Serializable, NetSerializable]
public sealed class AccessEncoderUiState : BoundUserInterfaceState
{
    public bool HasIdSlotHardware { get; }
    public bool HasModularSlot { get; }
    public bool SlotOccupied { get; }
    public string SlotCardLabel { get; }
    public string CardOwner { get; }
    public string CardJob { get; }
    public List<string> ScannedTags { get; }
    public string SelectedFilePath { get; }
    public string SelectedFilePreview { get; }
    public List<string> KeyFiles { get; }
    public string StatusMessage { get; }
    public bool IsLocked { get; }

    public AccessEncoderUiState(
        bool hasIdSlotHardware,
        bool hasModularSlot,
        bool slotOccupied,
        string slotCardLabel,
        string cardOwner,
        string cardJob,
        List<string> scannedTags,
        string selectedFilePath,
        string selectedFilePreview,
        List<string> keyFiles,
        string statusMessage,
        bool isLocked = false)
    {
        HasIdSlotHardware = hasIdSlotHardware;
        HasModularSlot = hasModularSlot;
        SlotOccupied = slotOccupied;
        SlotCardLabel = slotCardLabel;
        CardOwner = cardOwner;
        CardJob = cardJob;
        ScannedTags = scannedTags;
        SelectedFilePath = selectedFilePath;
        SelectedFilePreview = selectedFilePreview;
        KeyFiles = keyFiles;
        StatusMessage = statusMessage;
        IsLocked = isLocked;
    }
}

[Serializable, NetSerializable]
public enum AccessEncoderAction : byte
{
    Refresh,
    ToggleCardSlot,
    ReadSlotCard,
    BuildSaveFile,
    SelectFile,
    DeleteFile,
    ClearScanned
}

[Serializable, NetSerializable]
public sealed class AccessEncoderActionMessage : BoundUserInterfaceMessage
{
    public AccessEncoderAction Action { get; }
    public string Argument { get; }

    public AccessEncoderActionMessage(AccessEncoderAction action, string argument = "")
    {
        Action = action;
        Argument = argument;
    }
}
