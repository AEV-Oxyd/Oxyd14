using System.Numerics;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Payloads;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Computers;

[Serializable, NetSerializable]
public enum OxydComputerWireStatusKey : byte
{
    Power,
    RemoteControl,
    Network,
    Storage,
    DeviceLink
}

/// <summary>
/// Universal computer root component shared across Workstations, Laptops, PDAs, Routers,
/// APC controllers, Atmos controllers, and Autolathe embedded computers.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydComputerComponent : Component
{
    [DataField, AutoNetworkedField]
    public ComputerUiTemplate Template = ComputerUiTemplate.Desktop;

    [DataField, AutoNetworkedField]
    public List<EntityUid> OpenPrograms = new();

    [DataField, AutoNetworkedField]
    public Vector2 DefaultScreenSize = new(680f, 520f);

    [DataField, AutoNetworkedField]
    public ComputerBootState BootState = ComputerBootState.Running;

    [DataField, AutoNetworkedField]
    public string BootDiagnosticMessage = string.Empty;

    /// <summary>
    /// The first program started on boot (typically the OS or a dedicated controller program).
    /// This program stays running and cannot be closed while the computer is booted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? BootProgram;

    [DataField, AutoNetworkedField]
    public bool RequireHardDriveToBoot = true;

    [DataField, AutoNetworkedField]
    public bool RequireRamToBoot = true;

    [DataField, AutoNetworkedField]
    public bool RequirePowerToBoot = false;

    [DataField, AutoNetworkedField]
    public bool AllowRemoteControl = true;

    /// <summary>
    /// Automatically discover and link compatible DeviceLink ports on parent/nearby entities.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AutoLinkLocalPorts = true;

    /// <summary>
    /// If this computer is embedded inside a host machine (APC, Autolathe, AirAlarm),
    /// tracks the host machine entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? HostMachine;

    /// <summary>
    /// Motherboard wire cut states controlled by WiresSystem.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PowerWireCut;

    [DataField, AutoNetworkedField]
    public bool RemoteControlWireCut;

    [DataField, AutoNetworkedField]
    public bool NetworkWireCut;

    [DataField, AutoNetworkedField]
    public bool StorageWireCut;

    [DataField, AutoNetworkedField]
    public bool DeviceLinkWireCut;
}

/// <summary>
/// Attached to a host machine (such as an APC, Autolathe, or AirAlarm) that contains an embedded
/// computer entity inside its container.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmbeddedComputerHostComponent : Component
{
    [DataField]
    public EntProtoId ComputerPrototype = "BaseOxydEmbeddedController";

    [DataField]
    public string ContainerId = "computer_board";

    [DataField, AutoNetworkedField]
    public EntityUid? ComputerEntity;

    [DataField]
    public List<EntProtoId> DefaultPrograms = new();
}

/// <summary>
/// Per-entity state component attached to each Program (Cartridge) entity.
/// Allows convenient PVS control for remote applications and offloads per-program UI state
/// onto the program entity itself instead of bloating a single BUI state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ProgramStateComponent : Component
{
    [AutoNetworkedField]
    public BoundUserInterfaceState? State;

    [AutoNetworkedField]
    public NetEntity? LoaderEntity;
}

/// <summary>
/// Raised on a computer entity when a program entity or terminal executes a base console command
/// (e.g., to trigger a device link, send data through a port, or query hardware/network state).
/// </summary>
public sealed class ExecuteComputerCommandEvent : HandledEntityEventArgs
{
    public EntityUid? ProgramUid;
    public EntityUid? UserUid;
    public string CommandLine;
    public string Output = string.Empty;
    public bool Success;

    public ExecuteComputerCommandEvent(string commandLine, EntityUid? programUid = null, EntityUid? userUid = null)
    {
        CommandLine = commandLine;
        ProgramUid = programUid;
        UserUid = userUid;
    }
}

/// <summary>
/// Network payload sent over <see cref="Content.Shared.DeviceLinking.Systems.DeviceLinkSystem"/>
/// between a computer entity and its linked machine (Lathe, APC, AirAlarm, etc.).
/// </summary>
[Serializable, NetSerializable]
public partial record struct ComputerDeviceLinkPayload(
    string Command,
    string Argument = "",
    Dictionary<string, string>? Data = null) : ISignalNetworkPayload;

/// <summary>
/// Default OS program component. Always runs as the primary system shell unless another boot
/// program locks the device. Handles system settings, launching apps, device link management, and hardware inspection.
/// </summary>
[RegisterComponent]
public sealed partial class OsCartridgeComponent : Component
{
}

[Serializable, NetSerializable]
public sealed class OsProgramEntry
{
    public NetEntity Uid;
    public string Name = string.Empty;
    public string? Description;
    public SpriteSpecifier? Icon;
    public InstallationStatus InstallationStatus = InstallationStatus.Installed;
    public bool IsRunning;
    public bool IsBootProgram;
}

[Serializable, NetSerializable]
public sealed class OsHardwareEntry
{
    public string SlotName = string.Empty;
    public string PartName = string.Empty;
    public string Size = string.Empty;
    public string Details = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OsDeviceLinkEntry
{
    public string SourcePort = string.Empty;
    public string TargetEntityName = string.Empty;
    public NetEntity TargetEntity;
    public string SinkPort = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OsUiState : BoundUserInterfaceState
{
    public string DeviceGuid;
    public ComputerUiTemplate Template;
    public bool AllowRemoteControl;
    public int CpuLoadPercent;
    public int RamUsedMB;
    public int RamTotalMB;
    public int DiskUsedKB;
    public int DiskTotalKB;
    public int UploadBps;
    public int DownloadBps;
    public float BatteryPercent;
    public bool HasBattery;
    public List<OsProgramEntry> Programs;
    public List<OsHardwareEntry> Hardware;
    public List<OsDeviceLinkEntry> LinkedPorts;
    public string StatusMessage;
    public PdaIdInfoText PdaOwnerInfo;
    public string? StationName;
    public string? SshPassword;
    public bool HasCaptainAccess;
    public bool CanPlayMusic;
    public bool HasUplink;

    public bool FlashlightEnabled;
    public bool HasFlashlight;
    public bool HasPen;
    public bool HasPai;
    public bool HasDisk;
    public string? DiskName;
    public bool HasId;
    public NetEntity? ActiveProgram;

    public OsUiState(
        string deviceGuid,
        ComputerUiTemplate template,
        bool allowRemoteControl,
        int cpuLoadPercent,
        int ramUsedMB,
        int ramTotalMB,
        int diskUsedKB,
        int diskTotalKB,
        int uploadBps,
        int downloadBps,
        float batteryPercent,
        bool hasBattery,
        List<OsProgramEntry> programs,
        List<OsHardwareEntry> hardware,
        List<OsDeviceLinkEntry> linkedPorts,
        string statusMessage = "",
        PdaIdInfoText pdaOwnerInfo = default,
        string? stationName = null,
        string? sshPassword = null,
        bool hasCaptainAccess = false,
        bool canPlayMusic = false,
        bool hasUplink = false,
        bool flashlightEnabled = false,
        bool hasFlashlight = false,
        bool hasPen = false,
        bool hasPai = false,
        bool hasDisk = false,
        string? diskName = null,
        bool hasId = false,
        NetEntity? activeProgram = null)
    {
        DeviceGuid = deviceGuid;
        Template = template;
        AllowRemoteControl = allowRemoteControl;
        CpuLoadPercent = cpuLoadPercent;
        RamUsedMB = ramUsedMB;
        RamTotalMB = ramTotalMB;
        DiskUsedKB = diskUsedKB;
        DiskTotalKB = diskTotalKB;
        UploadBps = uploadBps;
        DownloadBps = downloadBps;
        BatteryPercent = batteryPercent;
        HasBattery = hasBattery;
        Programs = programs;
        Hardware = hardware;
        LinkedPorts = linkedPorts;
        StatusMessage = statusMessage;
        PdaOwnerInfo = pdaOwnerInfo;
        StationName = stationName;
        SshPassword = sshPassword;
        HasCaptainAccess = hasCaptainAccess;
        CanPlayMusic = canPlayMusic;
        HasUplink = hasUplink;
        FlashlightEnabled = flashlightEnabled;
        HasFlashlight = hasFlashlight;
        HasPen = hasPen;
        HasPai = hasPai;
        HasDisk = hasDisk;
        DiskName = diskName;
        HasId = hasId;
        ActiveProgram = activeProgram;
    }
}

[Serializable, NetSerializable]
public enum OsUiAction : byte
{
    LaunchProgram,
    CloseProgram,
    InstallProgram,
    UninstallProgram,
    ToggleRemoteControl,
    ScanAndLinkDevices,
    RunCommand,
    Refresh,
    ShowRingtone,
    ShowMusic,
    ShowUplink,
    LockUplink,
    ToggleFlashlight,
    EjectId,
    EjectPen,
    EjectPai,
    EjectDisk,
    PowerOff,
    InsertId,
    InsertPen,
    InsertPai,
    InsertDisk
}

[Serializable, NetSerializable]
public sealed class OsUiMessageEvent : CartridgeMessageEvent
{
    public OsUiAction Action;
    public NetEntity? TargetProgram;
    public string Command = string.Empty;

    public OsUiMessageEvent(OsUiAction action, NetEntity? targetProgram = null, string command = "")
    {
        Action = action;
        TargetProgram = targetProgram;
        Command = command;
    }
}

/// <summary>
/// Router management program component. Allows inspecting active connections, connected devices,
/// packet logs, DNS records, closing connections, and blacklisting GUIDs.
/// </summary>
[RegisterComponent]
public sealed partial class RouterCartridgeComponent : Component
{
}

[Serializable, NetSerializable]
public sealed class RouterConnectionView
{
    public string ConnectionId = string.Empty;
    public string SourceGuid = string.Empty;
    public string TargetGuid = string.Empty;
    public string ConnectionType = string.Empty;
    public string Description = string.Empty;
}

[Serializable, NetSerializable]
public sealed class RouterPacketLogView
{
    public uint PacketId;
    public string SenderGuid = string.Empty;
    public string TargetGuid = string.Empty;
    public string PacketType = string.Empty;
    public bool IsEncrypted;
    public string DecipheredSummary = string.Empty;
}

[Serializable, NetSerializable]
public sealed class RouterUiState : BoundUserInterfaceState
{
    public string RouterGuid;
    public List<RouterConnectionView> ActiveConnections;
    public List<string> ConnectedDevices;
    public List<string> BlacklistedGuids;
    public List<RouterPacketLogView> PacketLogs;
    public List<string> ConnectionLogs;
    public Dictionary<string, string> DnsRecords;

    public RouterUiState(
        string routerGuid,
        List<RouterConnectionView> activeConnections,
        List<string> connectedDevices,
        List<string> blacklistedGuids,
        List<RouterPacketLogView> packetLogs,
        List<string> connectionLogs,
        Dictionary<string, string> dnsRecords)
    {
        RouterGuid = routerGuid;
        ActiveConnections = activeConnections;
        ConnectedDevices = connectedDevices;
        BlacklistedGuids = blacklistedGuids;
        PacketLogs = packetLogs;
        ConnectionLogs = connectionLogs;
        DnsRecords = dnsRecords;
    }
}

[Serializable, NetSerializable]
public enum RouterUiAction : byte
{
    Refresh,
    CloseConnection,
    BlacklistGuid,
    UnblacklistGuid,
    RegisterDns,
    ClearLogs
}

[Serializable, NetSerializable]
public sealed class RouterUiMessageEvent : CartridgeMessageEvent
{
    public RouterUiAction Action;
    public string TargetId;
    public string ExtraValue;

    public RouterUiMessageEvent(RouterUiAction action, string targetId = "", string extraValue = "")
    {
        Action = action;
        TargetId = targetId;
        ExtraValue = extraValue;
    }
}

/// <summary>
/// Packet Sniffer & Re-emitter program component. Captures packets on the local network/router,
/// displays plaintext if unencrypted (or ciphertext if encrypted), and allows modifying and re-emitting unencrypted packets.
/// </summary>
[RegisterComponent]
public sealed partial class PacketSnifferCartridgeComponent : Component
{
    [DataField]
    public bool IsCapturing = true;
}

[Serializable, NetSerializable]
public sealed class CapturedPacketView
{
    public uint PacketId;
    public string SenderGuid = string.Empty;
    public string TargetGuid = string.Empty;
    public string PacketType = string.Empty;
    public bool IsEncrypted;
    public string PayloadPreview = string.Empty;
    public Dictionary<string, string> Parameters = new();
}

[Serializable, NetSerializable]
public sealed class PacketSnifferUiState : BoundUserInterfaceState
{
    public bool IsCapturing;
    public List<CapturedPacketView> Packets;
    public string StatusMessage;

    public PacketSnifferUiState(bool isCapturing, List<CapturedPacketView> packets, string statusMessage = "")
    {
        IsCapturing = isCapturing;
        Packets = packets;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum PacketSnifferUiAction : byte
{
    ToggleCapture,
    Refresh,
    Clear,
    ReemitPacket
}

[Serializable, NetSerializable]
public sealed class PacketSnifferUiMessageEvent : CartridgeMessageEvent
{
    public PacketSnifferUiAction Action;
    public uint PacketId;
    public string TargetGuid;
    public string ModifiedPayload;

    public PacketSnifferUiMessageEvent(PacketSnifferUiAction action, uint packetId = 0, string targetGuid = "", string modifiedPayload = "")
    {
        Action = action;
        PacketId = packetId;
        TargetGuid = targetGuid;
        ModifiedPayload = modifiedPayload;
    }
}

/// <summary>
/// Autolathe control program component. Links to the Lathe via DeviceLinkSystem through terminal commands,
/// reads designs from a data disk or internal storage, displays materials and queue status, and sends print/cancel commands.
/// </summary>
[RegisterComponent]
public sealed partial class LatheControlCartridgeComponent : Component
{
    [DataField]
    public EntityUid? LinkedLathe;
}

[Serializable, NetSerializable]
public sealed class LatheDesignView
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string MaterialsSummary = string.Empty;
    public bool CanProduce;
}

[Serializable, NetSerializable]
public sealed class LatheControlUiState : BoundUserInterfaceState
{
    public bool IsLinked;
    public string LatheName;
    public string CurrentProducing;
    public List<string> QueueItems;
    public Dictionary<string, int> Materials;
    public List<LatheDesignView> Designs;
    public string StatusMessage;

    public LatheControlUiState(
        bool isLinked,
        string latheName,
        string currentProducing,
        List<string> queueItems,
        Dictionary<string, int> materials,
        List<LatheDesignView> designs,
        string statusMessage = "")
    {
        IsLinked = isLinked;
        LatheName = latheName;
        CurrentProducing = currentProducing;
        QueueItems = queueItems;
        Materials = materials;
        Designs = designs;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum LatheControlUiAction : byte
{
    LinkLathe,
    SyncDiskDesigns,
    PrintDesign,
    CancelQueue,
    Refresh
}

[Serializable, NetSerializable]
public sealed class LatheControlUiMessageEvent : CartridgeMessageEvent
{
    public LatheControlUiAction Action;
    public string DesignId;
    public int Amount;

    public LatheControlUiMessageEvent(LatheControlUiAction action, string designId = "", int amount = 1)
    {
        Action = action;
        DesignId = designId;
        Amount = amount;
    }
}

/// <summary>
/// Area Power Controller (APC) control program component. Sends terminal commands through the computer
/// entity to toggle main breaker / channels and monitor charge over DeviceLinkSystem.
/// </summary>
[RegisterComponent]
public sealed partial class ApcControlCartridgeComponent : Component
{
    [DataField]
    public EntityUid? LinkedApc;
}

[Serializable, NetSerializable]
public sealed class ApcControlUiState : BoundUserInterfaceState
{
    public bool IsLinked;
    public string ApcName;
    public bool MainBreakerEnabled;
    public float ChargePercent;
    public int LoadWatts;
    public string ExternalPowerState;
    public string StatusMessage;

    public ApcControlUiState(
        bool isLinked,
        string apcName,
        bool mainBreakerEnabled,
        float chargePercent,
        int loadWatts,
        string externalPowerState,
        string statusMessage = "")
    {
        IsLinked = isLinked;
        ApcName = apcName;
        MainBreakerEnabled = mainBreakerEnabled;
        ChargePercent = chargePercent;
        LoadWatts = loadWatts;
        ExternalPowerState = externalPowerState;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum ApcControlUiAction : byte
{
    LinkApc,
    ToggleMainBreaker,
    Refresh
}

[Serializable, NetSerializable]
public sealed class ApcControlUiMessageEvent : CartridgeMessageEvent
{
    public ApcControlUiAction Action;

    public ApcControlUiMessageEvent(ApcControlUiAction action)
    {
        Action = action;
    }
}

/// <summary>
/// Atmospheric Controller (Air Alarm) program component. Sends terminal commands through the computer
/// entity to query air telemetry and change alarm modes over DeviceLinkSystem.
/// </summary>
[RegisterComponent]
public sealed partial class AtmosControlCartridgeComponent : Component
{
    [DataField]
    public EntityUid? LinkedAirAlarm;
}

[Serializable, NetSerializable]
public sealed class AtmosControlUiState : BoundUserInterfaceState
{
    public bool IsLinked;
    public string AlarmName;
    public float PressureKpa;
    public float TemperatureKelvin;
    public string CurrentMode;
    public string DangerLevel;
    public string StatusMessage;

    public AtmosControlUiState(
        bool isLinked,
        string alarmName,
        float pressureKpa,
        float temperatureKelvin,
        string currentMode,
        string dangerLevel,
        string statusMessage = "")
    {
        IsLinked = isLinked;
        AlarmName = alarmName;
        PressureKpa = pressureKpa;
        TemperatureKelvin = temperatureKelvin;
        CurrentMode = currentMode;
        DangerLevel = dangerLevel;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum AtmosControlUiAction : byte
{
    LinkAlarm,
    SetModeFiltering,
    SetModeFill,
    SetModePanic,
    SetModeNone,
    Refresh
}

[Serializable, NetSerializable]
public sealed class AtmosControlUiMessageEvent : CartridgeMessageEvent
{
    public AtmosControlUiAction Action;

    public AtmosControlUiMessageEvent(AtmosControlUiAction action)
    {
        Action = action;
    }
}
