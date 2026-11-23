using Content.Shared.Access;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Program that extracts access codes from an inserted ID card (PDA/computer ID slot)
/// or an ID card swiped directly onto the host computer, and saves them as .acc files
/// in /keys/ on the computer's filesystem.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AccessEncoderProgramComponent : Component
{
    public const string DefaultDirectory = "/keys/";

    [DataField]
    public bool HasScannedCard;

    [DataField]
    public string ScannedSourceLabel = string.Empty;

    [DataField]
    public string ScannedOwnerName = string.Empty;

    [DataField]
    public string ScannedJobTitle = string.Empty;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> ScannedAccessTags = new();

    [ViewVariables]
    public EntityUid? LastScannedCardUid;

    [DataField]
    public string? SelectedFilePath;

    [DataField]
    public string StatusMessage = "Insert an ID card into your PDA or swipe an ID card on this computer.";

    [DataField]
    public SoundSpecifier? SoundScan = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> AccessLevels = new()
    {
        "Armory",
        "Atmospherics",
        "Bar",
        "Brig",
        "Detective",
        "Captain",
        "Cargo",
        "Chapel",
        "Chemistry",
        "ChiefEngineer",
        "ChiefMedicalOfficer",
        "Command",
        "Cryogenics",
        "Engineering",
        "External",
        "GenpopEnter",
        "GenpopLeave",
        "HeadOfPersonnel",
        "HeadOfSecurity",
        "Hydroponics",
        "Janitor",
        "Kitchen",
        "Lawyer",
        "Maintenance",
        "Medical",
        "Quartermaster",
        "Research",
        "ResearchDirector",
        "Salvage",
        "Security",
        "Service",
        "Theatre",
    };
}

[Serializable, NetSerializable]
public sealed class AccessCodeFileEntryDto
{
    public string FilePath { get; }
    public string FileName { get; }
    public string OwnerName { get; }
    public string JobTitle { get; }
    public List<string> AccessTags { get; }

    public AccessCodeFileEntryDto(
        string filePath,
        string fileName,
        string ownerName,
        string jobTitle,
        List<string> accessTags)
    {
        FilePath = filePath;
        FileName = fileName;
        OwnerName = ownerName;
        JobTitle = jobTitle;
        AccessTags = accessTags;
    }
}

[Serializable, NetSerializable]
public sealed class AccessEncoderUiState : BoundUserInterfaceState
{
    public bool HasHardDrive { get; }
    public bool HasSourceCard { get; }
    public bool UsingScannedCard { get; }
    public string SourceLabel { get; }
    public string SourceOwnerName { get; }
    public string SourceJobTitle { get; }
    public List<string> SourceAccessTags { get; }
    public List<AccessCodeFileEntryDto> SavedFiles { get; }
    public string? SelectedFilePath { get; }
    public string? SelectedFileSummary { get; }
    public string StatusMessage { get; }

    // ID Modification / Authorization Console fields
    public bool HasPrivilegedAuth { get; }
    public string PrivilegedAuthLabel { get; }
    public List<string> AllowedAccessTags { get; }
    public bool HasTargetCard { get; }
    public string TargetCardLabel { get; }
    public string TargetFullName { get; }
    public string TargetJobTitle { get; }
    public List<string> TargetAccessTags { get; }
    public List<string> ConsoleAccessLevels { get; }
    public bool HasHostIdSlots { get; }
    public bool HasPrivilegedSlotCard { get; }
    public bool HasTargetSlotCard { get; }

    public AccessEncoderUiState(
        bool hasHardDrive,
        bool hasSourceCard,
        bool usingScannedCard,
        string sourceLabel,
        string sourceOwnerName,
        string sourceJobTitle,
        List<string> sourceAccessTags,
        List<AccessCodeFileEntryDto> savedFiles,
        string? selectedFilePath,
        string? selectedFileSummary,
        string statusMessage,
        bool hasPrivilegedAuth = false,
        string privilegedAuthLabel = "None",
        List<string>? allowedAccessTags = null,
        bool hasTargetCard = false,
        string targetCardLabel = "None",
        string targetFullName = "",
        string targetJobTitle = "",
        List<string>? targetAccessTags = null,
        List<string>? consoleAccessLevels = null,
        bool hasHostIdSlots = false,
        bool hasPrivilegedSlotCard = false,
        bool hasTargetSlotCard = false)
    {
        HasHardDrive = hasHardDrive;
        HasSourceCard = hasSourceCard;
        UsingScannedCard = usingScannedCard;
        SourceLabel = sourceLabel;
        SourceOwnerName = sourceOwnerName;
        SourceJobTitle = sourceJobTitle;
        SourceAccessTags = sourceAccessTags;
        SavedFiles = savedFiles;
        SelectedFilePath = selectedFilePath;
        SelectedFileSummary = selectedFileSummary;
        StatusMessage = statusMessage;
        HasPrivilegedAuth = hasPrivilegedAuth;
        PrivilegedAuthLabel = privilegedAuthLabel;
        AllowedAccessTags = allowedAccessTags ?? new List<string>();
        HasTargetCard = hasTargetCard;
        TargetCardLabel = targetCardLabel;
        TargetFullName = targetFullName;
        TargetJobTitle = targetJobTitle;
        TargetAccessTags = targetAccessTags ?? new List<string>();
        ConsoleAccessLevels = consoleAccessLevels ?? new List<string>();
        HasHostIdSlots = hasHostIdSlots;
        HasPrivilegedSlotCard = hasPrivilegedSlotCard;
        HasTargetSlotCard = hasTargetSlotCard;
    }
}

[Serializable, NetSerializable]
public enum AccessEncoderAction : byte
{
    BuildSaveFile,
    Refresh,
    ClearScanned,
    SelectFile,
    DeleteFile,
    TogglePrivilegedSlot,
    ToggleTargetSlot,
    WriteTargetIdentity,
    ToggleTargetAccessTag,
    ApplyAccFileToTarget,
    GrantAllAllowedAccess,
    RevokeAllAllowedAccess
}

[Serializable, NetSerializable]
public sealed class AccessEncoderActionMessage : BoundUserInterfaceMessage
{
    public AccessEncoderAction Action { get; }
    public string Argument { get; }
    public string SecondaryArgument { get; }

    public AccessEncoderActionMessage(
        AccessEncoderAction action,
        string argument = "",
        string secondaryArgument = "")
    {
        Action = action;
        Argument = argument;
        SecondaryArgument = secondaryArgument;
    }
}
