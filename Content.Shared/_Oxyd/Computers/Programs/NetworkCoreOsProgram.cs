using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Specialized boot operating system for <c>StationNetworkRouter</c> (the station network core).
/// Automatically provisions and incrementally updates crew/station/criminal records on online
/// Data Servers when players join or depart, while hosting Router Admin, Camera directory status,
/// and system tools.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetworkCoreOsProgramComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CurrentTab;

    [DataField, AutoNetworkedField]
    public bool AutoSyncRecords = true;

    [DataField, AutoNetworkedField]
    public int AutoSyncCount;

    [DataField, AutoNetworkedField]
    public string LastSyncSummary = "Core online — awaiting station events.";

    [DataField]
    public bool DefaultAppsLaunched;

    [ViewVariables]
    public List<string> SyncLogs = new();
}

[Serializable, NetSerializable]
public enum NetworkCoreOsAction : byte
{
    Refresh,
    SetTab,
    ToggleAutoSync,
    ForceSyncAllRecords,
    LaunchProgram,
    SwitchProgram,
    CloseProgram
}

[Serializable, NetSerializable]
public sealed class NetworkCoreOsActionMessage : BoundUserInterfaceMessage
{
    public NetworkCoreOsAction Action { get; }
    public int TabIndex { get; }
    public string ExecutablePath { get; }
    public NetEntity? ProgramEntity { get; }

    public NetworkCoreOsActionMessage(
        NetworkCoreOsAction action,
        int tabIndex = 0,
        string executablePath = "",
        NetEntity? programEntity = null)
    {
        Action = action;
        TabIndex = tabIndex;
        ExecutablePath = executablePath;
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class NetworkCoreOsUiState : BoundUserInterfaceState
{
    public string HostName { get; }
    public string DeviceGuid { get; }
    public int CurrentTab { get; }
    public bool AutoSyncRecords { get; }
    public int AutoSyncCount { get; }
    public string LastSyncSummary { get; }
    public bool DataServerOnline { get; }
    public string DataServerSummary { get; }
    public int ConnectedDevicesCount { get; }
    public int ConnectedCamerasCount { get; }
    public int ActiveConnectionsCount { get; }
    public int BlacklistedCount { get; }
    public int RecordedPacketsCount { get; }
    public OsHardwareSummaryDto Hardware { get; }
    public List<string> SyncLogs { get; }
    public List<OsProgramEntryDto> InstalledExecutables { get; }
    public List<OsProgramEntryDto> RunningPrograms { get; }
    public NetEntity? ActiveChildProgram { get; }

    public NetworkCoreOsUiState(
        string hostName,
        string deviceGuid,
        int currentTab,
        bool autoSyncRecords,
        int autoSyncCount,
        string lastSyncSummary,
        bool dataServerOnline,
        string dataServerSummary,
        int connectedDevicesCount,
        int connectedCamerasCount,
        int activeConnectionsCount,
        int blacklistedCount,
        int recordedPacketsCount,
        OsHardwareSummaryDto hardware,
        List<string> syncLogs,
        List<OsProgramEntryDto> installedExecutables,
        List<OsProgramEntryDto> runningPrograms,
        NetEntity? activeChildProgram)
    {
        HostName = hostName;
        DeviceGuid = deviceGuid;
        CurrentTab = currentTab;
        AutoSyncRecords = autoSyncRecords;
        AutoSyncCount = autoSyncCount;
        LastSyncSummary = lastSyncSummary;
        DataServerOnline = dataServerOnline;
        DataServerSummary = dataServerSummary;
        ConnectedDevicesCount = connectedDevicesCount;
        ConnectedCamerasCount = connectedCamerasCount;
        ActiveConnectionsCount = activeConnectionsCount;
        BlacklistedCount = blacklistedCount;
        RecordedPacketsCount = recordedPacketsCount;
        Hardware = hardware;
        SyncLogs = syncLogs;
        InstalledExecutables = installedExecutables;
        RunningPrograms = runningPrograms;
        ActiveChildProgram = activeChildProgram;
    }
}
