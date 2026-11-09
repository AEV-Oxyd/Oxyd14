using Content.Shared.PDA;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Device-agnostic remote screen descriptor capable of representing PDAs/CartridgeLoaders,
/// APCs, Atmospheric Alarms, Static Computers, or any BUI-backed electronic device,
/// as well as cycle metadata for Picture-in-Picture (PiP) cascade rendering.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteScreenState : BoundUserInterfaceState
{
    public NetEntity TargetEntity { get; }
    public string DeviceGuid { get; }
    public string DeviceName { get; }

    /// <summary>
    /// Primary BUI key for non-PDA devices (e.g. ApcUiKey.Key, SharedAirAlarmInterfaceKey.Key, Computer UI keys).
    /// </summary>
    public Enum? UiKey { get; }

    /// <summary>
    /// Client BUI type name from <see cref="InterfaceData.ClientType"/> for non-PDA devices.
    /// </summary>
    public string? BuiClientType { get; }

    /// <summary>
    /// Latest BUI state for non-PDA devices (APC, Air Alarm, Static Computer, etc.).
    /// </summary>
    public BoundUserInterfaceState? BuiState { get; }

    /// <summary>
    /// Full PDA state when the remote device is a PDA / CartridgeLoader.
    /// </summary>
    public PdaUpdateState? PdaState { get; }

    /// <summary>
    /// True when this node points to a device that is already an ancestor in the viewing chain
    /// (e.g. A -> B -> A or A -> A), signaling the client to render via Picture-in-Picture (PiP) cascade
    /// instead of physical widget parenting.
    /// </summary>
    public bool IsCycleReference { get; }

    /// <summary>
    /// If this remote device is itself viewing another device's screen, the target GUID being viewed.
    /// </summary>
    public string? ViewingTargetGuid { get; }
    public bool WasAlreadyOpen { get; set; }

    public RemoteScreenState(
        NetEntity targetEntity,
        string deviceGuid,
        string deviceName,
        Enum? uiKey = null,
        string? buiClientType = null,
        BoundUserInterfaceState? buiState = null,
        PdaUpdateState? pdaState = null,
        bool isCycleReference = false,
        string? viewingTargetGuid = null,
        bool wasAlreadyOpen = false)
    {
        TargetEntity = targetEntity;
        DeviceGuid = deviceGuid;
        DeviceName = deviceName;
        UiKey = uiKey;
        BuiClientType = buiClientType;
        BuiState = buiState;
        PdaState = pdaState;
        IsCycleReference = isCycleReference;
        ViewingTargetGuid = viewingTargetGuid;
        WasAlreadyOpen = wasAlreadyOpen;
    }
}
