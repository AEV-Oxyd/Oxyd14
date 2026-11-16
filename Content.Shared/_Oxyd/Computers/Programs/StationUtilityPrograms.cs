using Content.Shared.CrewManifest;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.MassMedia.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

// ============================================================================
// Crew Manifest Program (/records/crew_manifest.db)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class CrewManifestProgramComponent : Component
{
    public const string CacheFilePath = "/records/crew_manifest.db";

    [DataField]
    public string StatusMessage = "Ready";
}

[Serializable, NetSerializable]
public sealed class CrewManifestUiState : BoundUserInterfaceState
{
    public string StationName { get; }
    public CrewManifestEntries? Entries { get; }
    public bool IsOnlineSync { get; }
    public string StatusMessage { get; }

    public CrewManifestUiState(
        string stationName,
        CrewManifestEntries? entries,
        bool isOnlineSync = true,
        string statusMessage = "")
    {
        StationName = stationName;
        Entries = entries;
        IsOnlineSync = isOnlineSync;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public sealed class CrewManifestRefreshMessage : BoundUserInterfaceMessage;

// ============================================================================
// News Reader Program (/news/articles.db)
// ============================================================================

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NewsReaderProgramComponent : Component
{
    public const string CacheFilePath = "/news/articles.db";

    [DataField, AutoNetworkedField]
    public bool NotificationOn = true;

    [DataField, AutoNetworkedField]
    public int ArticleNumber;
}

[Serializable, NetSerializable]
public enum NewsReaderUiAction : byte
{
    Next,
    Prev,
    NotificationSwitch,
    SyncNetwork
}

[Serializable, NetSerializable]
public sealed class NewsReaderActionMessage : BoundUserInterfaceMessage
{
    public NewsReaderUiAction Action { get; }

    public NewsReaderActionMessage(NewsReaderUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public sealed class NewsReaderBoundUserInterfaceState : BoundUserInterfaceState
{
    public NewsArticle? Article { get; }
    public int TargetNum { get; }
    public int TotalNum { get; }
    public bool NotificationOn { get; }
    public string StatusMessage { get; }

    public NewsReaderBoundUserInterfaceState(
        NewsArticle? article,
        int targetNum,
        int totalNum,
        bool notificationOn,
        string statusMessage = "")
    {
        Article = article;
        TargetNum = targetNum;
        TotalNum = totalNum;
        NotificationOn = notificationOn;
        StatusMessage = statusMessage;
    }
}

// ============================================================================
// Wanted List Program (/records/wanted_list.db)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class WantedListProgramComponent : Component
{
    public const string CacheFilePath = "/records/wanted_list.db";

    [DataField]
    public string StatusMessage = "Ready";
}

[Serializable, NetSerializable]
public sealed class WantedListUiState : BoundUserInterfaceState
{
    public List<WantedRecord> Records { get; }
    public bool IsOnlineSync { get; }
    public string StatusMessage { get; }

    public WantedListUiState(List<WantedRecord> records, bool isOnlineSync = true, string statusMessage = "")
    {
        Records = records;
        IsOnlineSync = isOnlineSync;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public sealed class WantedListRefreshMessage : BoundUserInterfaceMessage;

// ============================================================================
// NetProbe Program (/logs/netprobe.db)
// ============================================================================

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetProbeProgramComponent : Component
{
    public const string CacheFilePath = "/logs/netprobe.db";

    [DataField, AutoNetworkedField]
    public int MaxSavedDevices = 9;

    [DataField]
    public SoundSpecifier? SoundScan = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class ProbedNetworkDevice
{
    public readonly string Name;
    public readonly string Address;
    public readonly string Frequency;
    public readonly string NetId;

    public ProbedNetworkDevice(string name, string address, string frequency, string netId)
    {
        Name = name;
        Address = address;
        Frequency = frequency;
        NetId = netId;
    }
}

[Serializable, NetSerializable]
public sealed class NetProbeUiState : BoundUserInterfaceState
{
    public List<ProbedNetworkDevice> ProbedDevices { get; }

    public NetProbeUiState(List<ProbedNetworkDevice> probedDevices)
    {
        ProbedDevices = probedDevices;
    }
}

[Serializable, NetSerializable]
public sealed class NetProbeClearMessage : BoundUserInterfaceMessage;

// ============================================================================
// LogProbe Program (/logs/logprobe.db)
// ============================================================================

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class LogProbeProgramComponent : Component
{
    public const string CacheFilePath = "/logs/logprobe.db";

    [DataField, AutoNetworkedField]
    public string EntityName = string.Empty;

    [DataField]
    public SoundSpecifier? SoundScan = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");

    [DataField]
    public SoundSpecifier? PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    [DataField]
    public EntProtoId PaperPrototype = "Paper";

    [DataField, AutoPausedField]
    public TimeSpan NextPrintAllowed = TimeSpan.Zero;

    [DataField]
    public TimeSpan PrintCooldown = TimeSpan.FromSeconds(5);
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class PulledAccessLog
{
    public readonly TimeSpan Time;
    public readonly string Accessor;

    public PulledAccessLog(TimeSpan time, string accessor)
    {
        Time = time;
        Accessor = accessor;
    }
}

[Serializable, NetSerializable]
public sealed class LogProbeUiState : BoundUserInterfaceState
{
    public string EntityName { get; }
    public List<PulledAccessLog> PulledLogs { get; }

    public LogProbeUiState(string entityName, List<PulledAccessLog> pulledLogs)
    {
        EntityName = entityName;
        PulledLogs = pulledLogs;
    }
}

[Serializable, NetSerializable]
public sealed class LogProbePrintMessage : BoundUserInterfaceMessage;

// ============================================================================
// Peripheral / Utility Capability Programs (MedTek & AstroNav)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class MedTekProgramComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class AstroNavProgramComponent : Component;

[Serializable, NetSerializable]
public sealed class UtilityProgramUiState : BoundUserInterfaceState
{
    public string Title { get; }
    public string Description { get; }
    public string Status { get; }

    public UtilityProgramUiState(string title, string description, string status)
    {
        Title = title;
        Description = description;
        Status = status;
    }
}
