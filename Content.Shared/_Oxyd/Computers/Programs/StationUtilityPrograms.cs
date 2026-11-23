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
    public TimeSpan? FileTimestamp { get; }

    public CrewManifestUiState(
        string stationName,
        CrewManifestEntries? entries,
        bool isOnlineSync = true,
        string statusMessage = "",
        TimeSpan? fileTimestamp = null)
    {
        StationName = stationName;
        Entries = entries;
        IsOnlineSync = isOnlineSync;
        StatusMessage = statusMessage;
        FileTimestamp = fileTimestamp;
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
    public TimeSpan? FileTimestamp { get; }

    public NewsReaderBoundUserInterfaceState(
        NewsArticle? article,
        int targetNum,
        int totalNum,
        bool notificationOn,
        string statusMessage = "",
        TimeSpan? fileTimestamp = null)
    {
        Article = article;
        TargetNum = targetNum;
        TotalNum = totalNum;
        NotificationOn = notificationOn;
        StatusMessage = statusMessage;
        FileTimestamp = fileTimestamp;
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
    public TimeSpan? FileTimestamp { get; }

    public WantedListUiState(
        List<WantedRecord> records,
        bool isOnlineSync = true,
        string statusMessage = "",
        TimeSpan? fileTimestamp = null)
    {
        Records = records;
        IsOnlineSync = isOnlineSync;
        StatusMessage = statusMessage;
        FileTimestamp = fileTimestamp;
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

// ============================================================================
// Cargo Orders Program (/bin/cargo_orders.nte) — Bank Account ID + PIN Auth & Withdrawal
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class CargoOrdersProgramComponent : Component
{
    [DataField]
    public string AuthenticatedAccountId = string.Empty;

    [DataField]
    public string AuthenticatedPin = string.Empty;

    [DataField]
    public string AuthenticatedAccountProto = string.Empty;

    [DataField]
    public bool IsAuthenticated;

    [DataField]
    public string SelectedCategory = "All";

    [DataField]
    public string StatusMessage = "Enter Bank Account ID and PIN to authenticate and withdraw funds for cargo orders.";
}

[Serializable, NetSerializable]
public sealed class CargoProductEntryDto
{
    public string ProductId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public int Cost { get; }

    public CargoProductEntryDto(string productId, string name, string description, string category, int cost)
    {
        ProductId = productId;
        Name = name;
        Description = description;
        Category = category;
        Cost = cost;
    }
}

[Serializable, NetSerializable]
public sealed class CargoOrderEntryDto
{
    public int OrderId { get; }
    public string ProductId { get; }
    public string ProductName { get; }
    public int Quantity { get; }
    public int TotalCost { get; }
    public string Requester { get; }
    public string Reason { get; }
    public string Account { get; }
    public bool Approved { get; }
    public string Approver { get; }

    public CargoOrderEntryDto(
        int orderId,
        string productId,
        string productName,
        int quantity,
        int totalCost,
        string requester,
        string reason,
        string account,
        bool approved,
        string approver)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        TotalCost = totalCost;
        Requester = requester;
        Reason = reason;
        Account = account;
        Approved = approved;
        Approver = approver;
    }
}

[Serializable, NetSerializable]
public sealed class CargoBankAccountSummaryDto
{
    public string AccountProto { get; }
    public string AccountId { get; }
    public int Balance { get; }

    public CargoBankAccountSummaryDto(string accountProto, string accountId, int balance)
    {
        AccountProto = accountProto;
        AccountId = accountId;
        Balance = balance;
    }
}

[Serializable, NetSerializable]
public enum CargoOrdersAction : byte
{
    Refresh,
    SelectCategory,
    AuthenticateBank,
    LogoutBank,
    PlaceAndPayOrder,
    QueueOrder,
    ApproveOrder,
    RemoveOrder,
    WithdrawCash
}

[Serializable, NetSerializable]
public sealed class CargoOrdersActionMessage : BoundUserInterfaceMessage
{
    public CargoOrdersAction Action { get; }
    public string AccountId { get; }
    public string Pin { get; }
    public string ProductId { get; }
    public int Quantity { get; }
    public int OrderId { get; }
    public string Requester { get; }
    public string Reason { get; }
    public string Category { get; }

    public CargoOrdersActionMessage(
        CargoOrdersAction action,
        string accountId = "",
        string pin = "",
        string productId = "",
        int quantity = 1,
        int orderId = 0,
        string requester = "",
        string reason = "",
        string category = "All")
    {
        Action = action;
        AccountId = accountId;
        Pin = pin;
        ProductId = productId;
        Quantity = quantity;
        OrderId = orderId;
        Requester = requester;
        Reason = reason;
        Category = category;
    }
}

[Serializable, NetSerializable]
public sealed class CargoOrdersUiState : BoundUserInterfaceState
{
    public bool IsAuthenticated { get; }
    public string AuthenticatedAccountId { get; }
    public string AuthenticatedAccountName { get; }
    public int AuthenticatedBalance { get; }
    public string SelectedCategory { get; }
    public string StatusMessage { get; }
    public List<string> Categories { get; }
    public List<CargoProductEntryDto> Products { get; }
    public List<CargoOrderEntryDto> Orders { get; }
    public List<CargoBankAccountSummaryDto> KnownAccounts { get; }

    public CargoOrdersUiState(
        bool isAuthenticated,
        string authenticatedAccountId,
        string authenticatedAccountName,
        int authenticatedBalance,
        string selectedCategory,
        string statusMessage,
        List<string> categories,
        List<CargoProductEntryDto> products,
        List<CargoOrderEntryDto> orders,
        List<CargoBankAccountSummaryDto>? knownAccounts = null)
    {
        IsAuthenticated = isAuthenticated;
        AuthenticatedAccountId = authenticatedAccountId;
        AuthenticatedAccountName = authenticatedAccountName;
        AuthenticatedBalance = authenticatedBalance;
        SelectedCategory = selectedCategory;
        StatusMessage = statusMessage;
        Categories = categories;
        Products = products;
        Orders = orders;
        KnownAccounts = knownAccounts ?? new List<CargoBankAccountSummaryDto>();
    }
}

// ============================================================================
// Crew Monitoring Program (/bin/crew_monitor.nte)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class CrewMonitorProgramComponent : Component;

[Serializable, NetSerializable]
public sealed class CrewSensorEntryDto
{
    public string Name { get; }
    public string JobTitle { get; }
    public bool IsAlive { get; }
    public int TotalDamage { get; }
    public string CoordinatesText { get; }

    public CrewSensorEntryDto(string name, string jobTitle, bool isAlive, int totalDamage, string coordinatesText)
    {
        Name = name;
        JobTitle = jobTitle;
        IsAlive = isAlive;
        TotalDamage = totalDamage;
        CoordinatesText = coordinatesText;
    }
}

[Serializable, NetSerializable]
public sealed class CrewMonitorUiState : BoundUserInterfaceState
{
    public string StationName { get; }
    public List<CrewSensorEntryDto> Sensors { get; }

    public CrewMonitorUiState(string stationName, List<CrewSensorEntryDto> sensors)
    {
        StationName = stationName;
        Sensors = sensors;
    }
}

[Serializable, NetSerializable]
public sealed class CrewMonitorRefreshMessage : BoundUserInterfaceMessage;

// ============================================================================
// Communications Console Program (/bin/comms_console.nte)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class CommsConsoleProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Ready for station broadcast.";
}

[Serializable, NetSerializable]
public enum CommsConsoleAction : byte
{
    Refresh,
    BroadcastAnnouncement,
    SetAlertLevel
}

[Serializable, NetSerializable]
public sealed class CommsConsoleActionMessage : BoundUserInterfaceMessage
{
    public CommsConsoleAction Action { get; }
    public string Text { get; }

    public CommsConsoleActionMessage(CommsConsoleAction action, string text = "")
    {
        Action = action;
        Text = text;
    }
}

[Serializable, NetSerializable]
public sealed class CommsConsoleUiState : BoundUserInterfaceState
{
    public bool CanBroadcast { get; }
    public string CurrentAlertLevel { get; }
    public List<string> AvailableAlertLevels { get; }
    public string StatusMessage { get; }

    public CommsConsoleUiState(
        bool canBroadcast,
        string currentAlertLevel,
        List<string> availableAlertLevels,
        string statusMessage)
    {
        CanBroadcast = canBroadcast;
        CurrentAlertLevel = currentAlertLevel;
        AvailableAlertLevels = availableAlertLevels;
        StatusMessage = statusMessage;
    }
}

// ============================================================================
// Solar Control Console Program (/bin/solar_control.nte)
// ============================================================================

[RegisterComponent, NetworkedComponent]
public sealed partial class SolarControlProgramComponent : Component
{
    [DataField]
    public string StatusMessage = "Solar array telemetry synced.";
}

[Serializable, NetSerializable]
public enum SolarControlAction : byte
{
    Refresh,
    SetAngle,
    SetVelocity
}

[Serializable, NetSerializable]
public sealed class SolarControlActionMessage : BoundUserInterfaceMessage
{
    public SolarControlAction Action { get; }
    public float ValueDegrees { get; }

    public SolarControlActionMessage(SolarControlAction action, float valueDegrees = 0f)
    {
        Action = action;
        ValueDegrees = valueDegrees;
    }
}

[Serializable, NetSerializable]
public sealed class SolarControlUiState : BoundUserInterfaceState
{
    public float PanelAngleDegrees { get; }
    public float AngularVelocityDegreesPerMin { get; }
    public float SunAngleDegrees { get; }
    public float TotalPowerWatts { get; }
    public string StatusMessage { get; }

    public SolarControlUiState(
        float panelAngleDegrees,
        float angularVelocityDegreesPerMin,
        float sunAngleDegrees,
        float totalPowerWatts,
        string statusMessage)
    {
        PanelAngleDegrees = panelAngleDegrees;
        AngularVelocityDegreesPerMin = angularVelocityDegreesPerMin;
        SunAngleDegrees = sunAngleDegrees;
        TotalPowerWatts = totalPowerWatts;
        StatusMessage = statusMessage;
    }
}
