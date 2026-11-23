using Content.Shared.Access;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cameras;

/// <summary>
/// Attached to a player entity while actively viewing one or more surveillance cameras.
/// Used exclusively by <c>ExamineSystemShared</c> / client <c>ExamineSystem</c> so viewers
/// can inspect entities visible through a camera feed without granting physical interaction reach.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveCameraViewerComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<NetEntity> ActiveCameras = new();
}

/// <summary>
/// Marker component on the physical Camera Microphone Upgrade module item.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CameraMicrophoneUpgradeComponent : Component
{
    [DataField]
    public float HearingRange = 10f;
}

/// <summary>
/// Active-component marker placed on powered, operational surveillance cameras so periodic
/// sighting scans only iterate active cameras (Active Component ECS pattern).
/// </summary>
[RegisterComponent]
public sealed partial class ActiveCameraWitnessComponent : Component
{
    [ViewVariables]
    public TimeSpan NextSightingScan;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> RecentSightings = new();

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> RecentCombatLogs = new();
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class CameraLogEntry
{
    [DataField]
    public TimeSpan Timestamp;

    [DataField]
    public string Category = "SYS";

    [DataField]
    public string Message = string.Empty;

    [DataField]
    public int ByteSize = 48;
}

/// <summary>
/// Bounded timestamped witness-log storage and Wi-Fi upgrade state for surveillance cameras.
/// Logs are evicted FIFO once <see cref="MaxMemoryBytes"/> is exceeded and can be extracted
/// remotely via the Surveillance Camera App or locally onto a Data Disk.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurveillanceCameraLogComponent : Component
{
    public const string MicrophoneSlotId = "camera_mic_slot";

    [DataField, AutoNetworkedField]
    public int MaxMemoryBytes = 4096;

    [DataField, AutoNetworkedField]
    public int UsedMemoryBytes;

    [DataField, AutoNetworkedField]
    public int DroppedEntries;

    [DataField]
    public float WitnessRange = 8.5f;

    [DataField, AutoNetworkedField]
    public bool HasMicrophoneUpgrade;

    [DataField]
    public List<CameraLogEntry> Entries = new();
}

/// <summary>
/// NTOS Surveillance Camera App (/bin/cameras.nte) program component.
/// Connects via the closest operational StationNetworkRouter to up to <see cref="MaxActiveCameras"/>
/// Wi-Fi surveillance cameras simultaneously, displays live camera feeds (with full examine support),
/// live audio when mic-upgraded, and downloads camera witness logs into local /logs/.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurveillanceCameraProgramComponent : Component
{
    public const int MaxActiveCameras = 6;

    [DataField, AutoNetworkedField]
    public string SelectedZone = "All";

    /// <summary>
    /// Currently focused camera feed (drives logs/audio inspector and backward-compatible state).
    /// </summary>
    [DataField, AutoNetworkedField]
    public NetEntity? ActiveCamera;

    /// <summary>
    /// Ordered list of up to <see cref="MaxActiveCameras"/> simultaneously watched cameras.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<NetEntity> ActiveCameras = new();

    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;

    [DataField, AutoNetworkedField]
    public bool IsAuthorized;

    [DataField, AutoNetworkedField]
    public string AuthorizedByLabel = string.Empty;

    [DataField, AutoNetworkedField]
    public string? SelectedAccFilePath;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> RequiredAccess = new()
    {
        "Security",
        "Command",
        "Detective",
        "Brig",
        "HeadOfSecurity",
        "Captain",
        "Armory"
    };

    [ViewVariables]
    public EntityUid? ActiveCameraUid;

    [ViewVariables]
    public List<EntityUid> ActiveCameraUids = new();

    [ViewVariables]
    public HashSet<EntityUid> ActiveViewers = new();

    [ViewVariables]
    public Dictionary<EntityUid, List<CameraAudioLineDto>> LiveAudioByCamera = new();

    [ViewVariables]
    public List<CameraAudioLineDto> LiveAudioFeed = new();
}

[Serializable, NetSerializable]
public sealed class ActiveCameraFeedDto
{
    public NetEntity CameraEntity { get; }
    public string CameraGuid { get; }
    public string Name { get; }
    public string Zone { get; }
    public bool HasMicrophone { get; }
    public int LogCount { get; }
    public int UsedBytes { get; }
    public int MaxBytes { get; }
    public int DroppedLogs { get; }
    public List<CameraAudioLineDto> LiveAudioFeed { get; }

    public ActiveCameraFeedDto(
        NetEntity cameraEntity,
        string cameraGuid,
        string name,
        string zone,
        bool hasMicrophone,
        int logCount,
        int usedBytes,
        int maxBytes,
        int droppedLogs,
        List<CameraAudioLineDto> liveAudioFeed)
    {
        CameraEntity = cameraEntity;
        CameraGuid = cameraGuid;
        Name = name;
        Zone = zone;
        HasMicrophone = hasMicrophone;
        LogCount = logCount;
        UsedBytes = usedBytes;
        MaxBytes = maxBytes;
        DroppedLogs = droppedLogs;
        LiveAudioFeed = liveAudioFeed;
    }
}

[Serializable, NetSerializable]
public sealed class CameraDirectoryEntryDto
{
    public NetEntity CameraEntity { get; }
    public string CameraGuid { get; }
    public string Name { get; }
    public string Zone { get; }
    public bool Active { get; }
    public bool HasMicrophone { get; }
    public int LogCount { get; }
    public int UsedBytes { get; }
    public int MaxBytes { get; }

    public CameraDirectoryEntryDto(
        NetEntity cameraEntity,
        string cameraGuid,
        string name,
        string zone,
        bool active,
        bool hasMicrophone,
        int logCount,
        int usedBytes,
        int maxBytes)
    {
        CameraEntity = cameraEntity;
        CameraGuid = cameraGuid;
        Name = name;
        Zone = zone;
        Active = active;
        HasMicrophone = hasMicrophone;
        LogCount = logCount;
        UsedBytes = usedBytes;
        MaxBytes = maxBytes;
    }
}

[Serializable, NetSerializable]
public sealed class CameraAudioLineDto
{
    public TimeSpan Timestamp { get; }
    public string Speaker { get; }
    public string Message { get; }

    public CameraAudioLineDto(TimeSpan timestamp, string speaker, string message)
    {
        Timestamp = timestamp;
        Speaker = speaker;
        Message = message;
    }
}

[Serializable, NetSerializable]
public sealed class CameraLogEntryDto
{
    public TimeSpan Timestamp { get; }
    public string Category { get; }
    public string Message { get; }

    public CameraLogEntryDto(TimeSpan timestamp, string category, string message)
    {
        Timestamp = timestamp;
        Category = category;
        Message = message;
    }
}

[Serializable, NetSerializable]
public enum SurveillanceCameraAction : byte
{
    Refresh,
    SelectZone,
    SelectCamera,
    FocusCamera,
    DisconnectCamera,
    Disconnect,
    DownloadLogs,
    ClearLogs,
    SelectAccFile,
    AuthorizeWithAcc,
    RevokeAuth
}

[Serializable, NetSerializable]
public sealed class SurveillanceCameraActionMessage : BoundUserInterfaceMessage
{
    public SurveillanceCameraAction Action { get; }
    public string Zone { get; }
    public NetEntity? CameraEntity { get; }

    public SurveillanceCameraActionMessage(
        SurveillanceCameraAction action,
        string zone = "All",
        NetEntity? cameraEntity = null)
    {
        Action = action;
        Zone = zone;
        CameraEntity = cameraEntity;
    }
}

[Serializable, NetSerializable]
public sealed class SurveillanceCameraUiState : BoundUserInterfaceState
{
    public bool Online { get; }
    public string RouterLabel { get; }
    public string StatusMessage { get; }
    public string SelectedZone { get; }
    public NetEntity? ActiveCamera { get; }
    public string ActiveCameraName { get; }
    public string ActiveCameraGuid { get; }
    public string ActiveCameraZone { get; }
    public bool ActiveHasMicrophone { get; }
    public int ActiveLogUsedBytes { get; }
    public int ActiveLogMaxBytes { get; }
    public int ActiveDroppedLogs { get; }
    public List<string> Zones { get; }
    public List<CameraDirectoryEntryDto> Cameras { get; }
    public List<CameraLogEntryDto> ActiveLogs { get; }
    public List<CameraAudioLineDto> LiveAudioFeed { get; }
    public List<ActiveCameraFeedDto> ActiveFeeds { get; }
    public int MaxActiveCameras { get; }
    public bool IsAuthorized { get; }
    public string AuthorizedByLabel { get; }
    public string? SelectedAccFilePath { get; }
    public List<string> AvailableAccFiles { get; }

    public SurveillanceCameraUiState(
        bool online,
        string routerLabel,
        string statusMessage,
        string selectedZone,
        NetEntity? activeCamera,
        string activeCameraName,
        string activeCameraGuid,
        string activeCameraZone,
        bool activeHasMicrophone,
        int activeLogUsedBytes,
        int activeLogMaxBytes,
        int activeDroppedLogs,
        List<string> zones,
        List<CameraDirectoryEntryDto> cameras,
        List<CameraLogEntryDto> activeLogs,
        List<CameraAudioLineDto> liveAudioFeed,
        List<ActiveCameraFeedDto>? activeFeeds = null,
        int maxActiveCameras = SurveillanceCameraProgramComponent.MaxActiveCameras,
        bool isAuthorized = false,
        string authorizedByLabel = "",
        string? selectedAccFilePath = null,
        List<string>? availableAccFiles = null)
    {
        Online = online;
        RouterLabel = routerLabel;
        StatusMessage = statusMessage;
        SelectedZone = selectedZone;
        ActiveCamera = activeCamera;
        ActiveCameraName = activeCameraName;
        ActiveCameraGuid = activeCameraGuid;
        ActiveCameraZone = activeCameraZone;
        ActiveHasMicrophone = activeHasMicrophone;
        ActiveLogUsedBytes = activeLogUsedBytes;
        ActiveLogMaxBytes = activeLogMaxBytes;
        ActiveDroppedLogs = activeDroppedLogs;
        Zones = zones;
        Cameras = cameras;
        ActiveLogs = activeLogs;
        LiveAudioFeed = liveAudioFeed;
        ActiveFeeds = activeFeeds ?? new List<ActiveCameraFeedDto>();
        MaxActiveCameras = maxActiveCameras;
        IsAuthorized = isAuthorized;
        AuthorizedByLabel = authorizedByLabel;
        SelectedAccFilePath = selectedAccFilePath;
        AvailableAccFiles = availableAccFiles ?? new List<string>();
    }
}
