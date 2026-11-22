using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmbeddedOsProgramComponent : Component
{
    /// <summary>
    /// 0 = Active Child Program, 1 = System & Program Launcher, 2 = Mirrored DeviceLink Ports, 3 = Terminal/Settings.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentView;

    [DataField]
    public bool DefaultAppLaunched;
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsUiState : BoundUserInterfaceState
{
    public string Title { get; }
    public string Subtitle { get; }
    public string HostName { get; }
    public string DeviceGuid { get; }
    public int CurrentView { get; }
    public NetEntity? ActiveChildProgram { get; }
    public List<OsProgramEntryDto> InstalledExecutables { get; }
    public List<OsProgramEntryDto> RunningPrograms { get; }
    public List<MirroredDevicePortInfo> MirroredPorts { get; }
    public OsHardwareSummaryDto Hardware { get; }
    public bool HasLock { get; }
    public bool IsHostLocked { get; }
    public string? DefaultAppPath { get; }

    public EmbeddedOsUiState(
        string title,
        string subtitle,
        string hostName,
        string deviceGuid,
        int currentView,
        NetEntity? activeChildProgram,
        List<OsProgramEntryDto> installedExecutables,
        List<OsProgramEntryDto> runningPrograms,
        List<MirroredDevicePortInfo> mirroredPorts,
        OsHardwareSummaryDto hardware,
        bool hasLock = false,
        bool isHostLocked = false,
        string? defaultAppPath = null)
    {
        Title = title;
        Subtitle = subtitle;
        HostName = hostName;
        DeviceGuid = deviceGuid;
        CurrentView = currentView;
        ActiveChildProgram = activeChildProgram;
        InstalledExecutables = installedExecutables;
        RunningPrograms = runningPrograms;
        MirroredPorts = mirroredPorts;
        Hardware = hardware;
        HasLock = hasLock;
        IsHostLocked = isHostLocked;
        DefaultAppPath = defaultAppPath;
    }
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsSetViewMessage : BoundUserInterfaceMessage
{
    public int ViewIndex { get; }

    public EmbeddedOsSetViewMessage(int viewIndex)
    {
        ViewIndex = viewIndex;
    }
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsLaunchProgramMessage : BoundUserInterfaceMessage
{
    public string ExecutablePath { get; }

    public EmbeddedOsLaunchProgramMessage(string executablePath)
    {
        ExecutablePath = executablePath;
    }
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsSwitchProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public EmbeddedOsSwitchProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsCloseProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public EmbeddedOsCloseProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class EmbeddedOsInvokePortMessage : BoundUserInterfaceMessage
{
    public string PortId { get; }
    public bool IsHostPort { get; }
    public string Command { get; }
    public string Argument { get; }

    public EmbeddedOsInvokePortMessage(string portId, bool isHostPort, string command = "", string argument = "")
    {
        PortId = portId;
        IsHostPort = isHostPort;
        Command = command;
        Argument = argument;
    }
}
