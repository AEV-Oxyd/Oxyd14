using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated handheld Operating System program for PDAs (/bin/pda_os.nte -> OxydPdaOsProgram).
/// Provides a pocket-first dashboard for PDA hardware slots, station status, utilities,
/// quick-launch apps, and full child program hosting.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PdaOsProgramComponent : Component
{
    /// <summary>
    /// 0 = Home (Round time, Owner/ID & VFS Apps), 1 = Device & Hardware, 2 = Active App.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentView;
}

[Serializable, NetSerializable]
public sealed class PdaOsUiState : BoundUserInterfaceState
{
    public string HostTitle { get; }
    public string HostSubtitle { get; }
    public string BorderColor { get; }
    public string? OwnerName { get; }
    public string? OwnerJob { get; }
    public string? StationName { get; }
    public string? StationAlertLevel { get; }
    public Color StationAlertColor { get; }
    public string? InsertedIdName { get; }
    public string? InsertedPenName { get; }
    public string? InsertedPaiName { get; }
    public string? InsertedDiskName { get; }
    public bool FlashlightOn { get; }
    public bool HasRinger { get; }
    public bool HasInstrument { get; }
    public bool HasUplinkUnlocked { get; }
    public int CurrentView { get; }
    public NetEntity? ActiveChildProgram { get; }
    public List<OsProgramEntryDto> InstalledExecutables { get; }
    public List<OsProgramEntryDto> RunningPrograms { get; }
    public List<MirroredDevicePortInfo> DevicePorts { get; }
    public OsHardwareSummaryDto Hardware { get; }
    public string? OsAuthLabel { get; }

    public PdaOsUiState(
        string hostTitle,
        string hostSubtitle,
        string borderColor,
        string? ownerName,
        string? ownerJob,
        string? stationName,
        string? stationAlertLevel,
        Color stationAlertColor,
        string? insertedIdName,
        string? insertedPenName,
        string? insertedPaiName,
        string? insertedDiskName,
        bool flashlightOn,
        bool hasRinger,
        bool hasInstrument,
        bool hasUplinkUnlocked,
        int currentView,
        NetEntity? activeChildProgram,
        List<OsProgramEntryDto> installedExecutables,
        List<OsProgramEntryDto> runningPrograms,
        List<MirroredDevicePortInfo> devicePorts,
        OsHardwareSummaryDto hardware,
        string? osAuthLabel = null)
    {
        HostTitle = hostTitle;
        HostSubtitle = hostSubtitle;
        BorderColor = borderColor;
        OwnerName = ownerName;
        OwnerJob = ownerJob;
        StationName = stationName;
        StationAlertLevel = stationAlertLevel;
        StationAlertColor = stationAlertColor;
        InsertedIdName = insertedIdName;
        InsertedPenName = insertedPenName;
        InsertedPaiName = insertedPaiName;
        InsertedDiskName = insertedDiskName;
        FlashlightOn = flashlightOn;
        HasRinger = hasRinger;
        HasInstrument = hasInstrument;
        HasUplinkUnlocked = hasUplinkUnlocked;
        CurrentView = currentView;
        ActiveChildProgram = activeChildProgram;
        InstalledExecutables = installedExecutables;
        RunningPrograms = runningPrograms;
        DevicePorts = devicePorts;
        Hardware = hardware;
        OsAuthLabel = osAuthLabel;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsSetViewMessage : BoundUserInterfaceMessage
{
    public int ViewIndex { get; }

    public PdaOsSetViewMessage(int viewIndex)
    {
        ViewIndex = viewIndex;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsLaunchProgramMessage : BoundUserInterfaceMessage
{
    public string ExecutablePath { get; }

    public PdaOsLaunchProgramMessage(string executablePath)
    {
        ExecutablePath = executablePath;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsSwitchProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public PdaOsSwitchProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsCloseProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public PdaOsCloseProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsHardwareActionMessage : BoundUserInterfaceMessage
{
    public string Action { get; }
    public string Argument { get; }

    public PdaOsHardwareActionMessage(string action, string argument = "")
    {
        Action = action;
        Argument = argument;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsInvokeDevicePortMessage : BoundUserInterfaceMessage
{
    public string PortId { get; }
    public bool IsHostPort { get; }
    public string Command { get; }
    public string Argument { get; }

    public PdaOsInvokeDevicePortMessage(string portId, bool isHostPort, string command = "", string argument = "")
    {
        PortId = portId;
        IsHostPort = isHostPort;
        Command = command;
        Argument = argument;
    }
}

[Serializable, NetSerializable]
public sealed class PdaOsShutDownMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class PdaOsLogoutAuthMessage : BoundUserInterfaceMessage
{
}
