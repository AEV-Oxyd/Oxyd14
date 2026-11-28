using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared.Access;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Payloads;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers;

[Serializable, NetSerializable]
public enum OxydComputerWireStatusKey : byte
{
    Power,
    RemoteControl,
    Network,
    Storage,
    DeviceLink,
    BootProgram,
    Wires
}

[Serializable, NetSerializable]
public enum ComputerTemplate : byte
{
    Pda = 0,
    Desktop = 1,
    Embedded = 2
}

[Serializable, NetSerializable]
public enum ComputerBootState : byte
{
    Off = 0,
    NoPower = 1,
    WireCut = 2,
    NoHardDrive = 3,
    NoRam = 4,
    NoBootProgram = 5,
    Running = 6,
    NoCpu = 7
}

/// <summary>
/// DeviceLink port descriptor mirroring a port on either the computer or its represented host machine.
/// </summary>
[Serializable, NetSerializable]
public sealed class MirroredDevicePortInfo
{
    public NetEntity OwnerNetEntity { get; }
    public string OwnerName { get; }
    public string PortId { get; }
    public string PortName { get; }
    public bool IsSource { get; }
    public bool IsHostPort { get; }
    public List<string> LinkedTargets { get; }

    public MirroredDevicePortInfo(
        NetEntity ownerNetEntity,
        string ownerName,
        string portId,
        string portName,
        bool isSource,
        bool isHostPort,
        List<string> linkedTargets)
    {
        OwnerNetEntity = ownerNetEntity;
        OwnerName = ownerName;
        PortId = portId;
        PortName = portName;
        IsSource = isSource;
        IsHostPort = isHostPort;
        LinkedTargets = linkedTargets;
    }
}

/// <summary>
/// Attached to every computer entity (which is always encapsulated separately from its host machine).
/// Manages hardware-bounded execution, boot-first-.nte policy, and DeviceLink port mirroring.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydComputerComponent : Component
{
    public const string ContainerPrograms = "program_container";

    [DataField, AutoNetworkedField]
    public ComputerTemplate Template = ComputerTemplate.Pda;

    [DataField, AutoNetworkedField]
    public Vector2i DefaultScreenSize = new(600, 480);

    [DataField, AutoNetworkedField]
    public bool AllowRemoteControl = true;

    [DataField, AutoNetworkedField]
    public bool AllowDeviceLink = true;

    /// <summary>
    /// Whether this device advertises itself to network probe / NetNavigator discovery queries.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool NetworkDiscoverable = true;

    [DataField, AutoNetworkedField]
    public bool IsPowered = true;

    [DataField, AutoNetworkedField]
    public bool RequirePowerToBoot = false;

    [DataField, AutoNetworkedField]
    public bool AutoBootWhenPowered = true;

    /// <summary>
    /// Physical hardware prototypes spawned into slots on map initialization.
    /// </summary>
    [DataField]
    public List<EntProtoId> DefaultHardware = new();

    /// <summary>
    /// Optional explicit startup app launched by EmbeddedOS after the boot program initializes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? EmbeddedDefaultAppPath;

    /// <summary>
    /// Back-reference to the host device entity (PDA, APC, AirAlarm, Autolathe, Workstation, Router).
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? HostMachine;

    [DataField, AutoNetworkedField]
    public string HostTitle = "NTOS 1.4";

    [DataField, AutoNetworkedField]
    public string HostSubtitle = " | Terminal";

    [DataField, AutoNetworkedField]
    public string BorderColor = "#00D4FF";

    [DataField, AutoNetworkedField]
    public string AccentHColor = "#00D4FF";

    [DataField, AutoNetworkedField]
    public string AccentVColor = "#005577";

    /// <summary>
    /// First .nte program spawned at boot. Has total ownership of the display UI.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? BootProgram;

    /// <summary>
    /// Currently focused foreground program launched by the OS / computer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ActiveProgram;

    [DataField, AutoNetworkedField]
    public List<EntityUid> RunningPrograms = new();

    [DataField, AutoNetworkedField]
    public ComputerBootState BootState = ComputerBootState.Off;

    [DataField, AutoNetworkedField]
    public bool IsBooted;

    [DataField, AutoNetworkedField]
    public bool ManuallyShutDown;

    [DataField, AutoNetworkedField]
    public string BootError = string.Empty;

    [DataField, AutoNetworkedField]
    public bool RequireIdAccess;

    [DataField, AutoNetworkedField]
    public bool LockIdSlot;

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

    [DataField, AutoNetworkedField]
    public HashSet<string> LockedDeviceLinkPorts = new();

    [DataField]
    public string SshPassword = string.Empty;

    /// <summary>
    /// When true, this computer remains locally accessible, but remote connections (SSH / remote screen)
    /// require either a configured password/key or an access level in <see cref="RemoteAccessTags"/>
    /// (defaulting to SystemAdmin on standard corporate NTOS computers and PDAs).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RemoteLocked;

    /// <summary>
    /// Access levels permitted to remotely manage this device when running a non-jailbroken OS.
    /// Defaults to SystemAdmin on standard NTOS installations.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> RemoteAccessTags = new() { "SystemAdmin" };

    /// <summary>
    /// Software security lock state configured via the Security Lock app (/bin/security_lock.nte).
    /// When true, the computer displays a full-screen lock prompt and blocks all program actions until unlocked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool SoftwareLocked;

    /// <summary>
    /// Server-only passphrase used to unlock this computer's software lock (and SSH).
    /// Intentionally not networked to clients.
    /// </summary>
    [DataField]
    public string LockPassword = string.Empty;

    /// <summary>
    /// Human-readable label for the bound key-based password (shown on the lock screen as a hint).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string LockKeyLabel = string.Empty;

    /// <summary>
    /// Short public fingerprint of the bound key-based password (safe to show on UI).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string LockKeyId = string.Empty;

    /// <summary>
    /// Server-only secret token matched against <see cref="FileSystem.PasswordKeyVirtualFile.KeySecret"/>
    /// (or entered directly) for key-based password unlock and key-based SSH auth.
    /// Intentionally not networked to clients.
    /// </summary>
    [DataField]
    public string LockKeySecret = string.Empty;

    /// <summary>
    /// Optional set of ID card access levels allowed to unlock this computer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> RequiredAccessTags = new();

    /// <summary>
    /// When true, an ID card / .acc file must satisfy ALL tags in <see cref="RequiredAccessTags"/>;
    /// when false, ANY single matching tag unlocks the computer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LockRequiresAllAccess = true;

    /// <summary>
    /// Transient feedback displayed on the software lock screen (e.g. "Invalid password or key.").
    /// </summary>
    [AutoNetworkedField]
    public string LockStatusMessage = string.Empty;

    /// <summary>
    /// Server-only monotonic counter incremented whenever this computer engages its software lock,
    /// reboots into a locked state, or changes lock credentials. Used to invalidate unauthenticated/stale SSH sessions.
    /// </summary>
    [DataField]
    public int LockEpoch;

    [ViewVariables]
    public bool InitializingHardware;

    [DataField, AutoNetworkedField]
    public bool DisableLocalShell;

    /// <summary>
    /// When true, this computer's display UI docks into the HUD's <c>ViewportContainer</c> and pauses
    /// normal world rendering while open locally (stationary consoles, workstations, servers, routers).
    /// Always false for handheld PDAs, tablets, and small embedded subsystem panels.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DockInViewport;

    /// <summary>
    /// OS-level authenticated user display name established by swiping an ID card on this computer.
    /// Read/control applications (Cameras, APC, Atmos, Station Records, Comms) inherit this session's
    /// access tags without requiring a physically retained ID card.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string OsAuthUserName = string.Empty;

    [DataField, AutoNetworkedField]
    public string OsAuthJobTitle = string.Empty;

    [DataField, AutoNetworkedField]
    public string OsAuthLabel = string.Empty;

    /// <summary>
    /// Server-side set of access tags granted by the currently authenticated OS user session.
    /// </summary>
    [ViewVariables]
    public HashSet<ProtoId<AccessLevelPrototype>> OsAuthAccessTags = new();
}

/// <summary>
/// Attached to host devices (PDAs, APCs, AirAlarms, Autolathes, Workstations, Routers, Servers)
/// to encapsulate their computer inside a dedicated child entity container.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmbeddedComputerHostComponent : Component
{
    public const string ContainerBoard = "computer_board";

    [DataField(required: true)]
    public EntProtoId ComputerPrototype = "BaseOxydEmbeddedController";

    [DataField, AutoNetworkedField]
    public EntityUid? ComputerEntity;

    /// <summary>
    /// Overrides the hardware spawned in the encapsulated computer if non-empty.
    /// </summary>
    [DataField]
    public List<EntProtoId> DefaultHardware = new();

    [DataField, AutoNetworkedField]
    public string? EmbeddedDefaultAppPath;

    [DataField]
    public string BorderColor = "#00D4FF";

    [DataField]
    public string AccentHColor = "#00D4FF";

    [DataField]
    public string AccentVColor = "#005577";

    [DataField]
    public string Title = "NTOS";

    [DataField]
    public string Subtitle = " | Embedded Subsystem";

    /// <summary>
    /// Optional explicit override controlling whether this host's computer display docks inside the
    /// HUD <c>ViewportContainer</c> and pauses world rendering when opened locally.
    /// </summary>
    [DataField]
    public bool? DockInViewport;

    /// <summary>
    /// Re-entrancy guard set while binding and booting the child computer entity.
    /// </summary>
    [ViewVariables]
    public bool BindingInProgress;
}

/// <summary>
/// Hardware-level computer display shell UI state. Never carries child program BUI states;
/// program entities use direct BUI updates on <see cref="ProgramUiKey.Key"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class ComputerDisplayUiState : BoundUserInterfaceState
{
    public string ComputerName { get; }
    public string DeviceGuid { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string BorderColor { get; }
    public string AccentHColor { get; }
    public string AccentVColor { get; }
    public Vector2i ScreenSize { get; }
    public bool IsBooted { get; }
    public string BootError { get; }
    public NetEntity? BootProgram { get; }
    public NetEntity? ActiveProgram { get; }
    public NetEntity? HostMachine { get; }
    public int CpuFrequencyMhz { get; }
    public int RamUsedMb { get; }
    public int RamTotalMb { get; }
    public int StorageUsedBytes { get; }
    public int StorageTotalBytes { get; }
    public bool HasCpu { get; }
    public bool HasRam { get; }
    public bool HasHardDrive { get; }
    public bool HasNetworkCard { get; }
    public bool HasDataPort { get; }
    public bool DockInViewport { get; }
    public bool IsSoftwareLocked { get; }
    public bool HasPasswordLock { get; }
    public bool HasKeyLock { get; }
    public string LockKeyLabel { get; }
    public List<string> RequiredAccessLabels { get; }
    public bool LockRequiresAllAccess { get; }
    public string LockStatusMessage { get; }
    public bool IsHostLocked { get; }
    public string HostDisplayName { get; }

    public ComputerDisplayUiState(
        string computerName,
        string deviceGuid,
        string title,
        string subtitle,
        string borderColor,
        string accentHColor,
        string accentVColor,
        Vector2i screenSize,
        bool isBooted,
        string bootError,
        NetEntity? bootProgram,
        NetEntity? activeProgram,
        NetEntity? hostMachine,
        int cpuFrequencyMhz,
        int ramUsedMb,
        int ramTotalMb,
        int storageUsedBytes,
        int storageTotalBytes,
        bool hasCpu,
        bool hasRam,
        bool hasHardDrive,
        bool hasNetworkCard,
        bool hasDataPort,
        bool dockInViewport = false,
        bool isSoftwareLocked = false,
        bool hasPasswordLock = false,
        bool hasKeyLock = false,
        string lockKeyLabel = "",
        List<string>? requiredAccessLabels = null,
        bool lockRequiresAllAccess = true,
        string lockStatusMessage = "",
        bool isHostLocked = false,
        string hostDisplayName = "")
    {
        ComputerName = computerName;
        DeviceGuid = deviceGuid;
        Title = title;
        Subtitle = subtitle;
        BorderColor = borderColor;
        AccentHColor = accentHColor;
        AccentVColor = accentVColor;
        ScreenSize = screenSize;
        IsBooted = isBooted;
        BootError = bootError;
        BootProgram = bootProgram;
        ActiveProgram = activeProgram;
        HostMachine = hostMachine;
        CpuFrequencyMhz = cpuFrequencyMhz;
        RamUsedMb = ramUsedMb;
        RamTotalMb = ramTotalMb;
        StorageUsedBytes = storageUsedBytes;
        StorageTotalBytes = storageTotalBytes;
        HasCpu = hasCpu;
        HasRam = hasRam;
        HasHardDrive = hasHardDrive;
        HasNetworkCard = hasNetworkCard;
        HasDataPort = hasDataPort;
        DockInViewport = dockInViewport;
        IsSoftwareLocked = isSoftwareLocked;
        HasPasswordLock = hasPasswordLock;
        HasKeyLock = hasKeyLock;
        LockKeyLabel = lockKeyLabel;
        RequiredAccessLabels = requiredAccessLabels ?? new List<string>();
        LockRequiresAllAccess = lockRequiresAllAccess;
        LockStatusMessage = lockStatusMessage;
        IsHostLocked = isHostLocked;
        HostDisplayName = hostDisplayName;
    }
}

[Serializable, NetSerializable]
public sealed class ComputerRetryBootMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ComputerUnlockWithPasswordMessage : BoundUserInterfaceMessage
{
    public string Secret { get; }

    public ComputerUnlockWithPasswordMessage(string secret)
    {
        Secret = secret;
    }
}

[Serializable, NetSerializable]
public sealed class ComputerUnlockWithKeyFileMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ComputerUnlockWithIdMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed partial class ComputerDeviceLinkPayload : ISignalNetworkPayload
{
    public string Command { get; set; } = string.Empty;
    public string Argument { get; set; } = string.Empty;
    public string SenderGuid { get; set; } = string.Empty;

    public ComputerDeviceLinkPayload()
    {
    }

    public ComputerDeviceLinkPayload(string command, string argument = "", string senderGuid = "")
    {
        Command = command;
        Argument = argument;
        SenderGuid = senderGuid;
    }

    public ISignalNetworkPayload Clone()
    {
        return new ComputerDeviceLinkPayload(Command, Argument, SenderGuid);
    }
}

[ByRefEvent]
public readonly record struct ExecuteComputerCommandEvent(
    EntityUid ComputerUid,
    string Command,
    bool Handled = false,
    string Output = "");
