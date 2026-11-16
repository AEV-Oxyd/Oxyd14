using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Describes a live remote computer screen session attached inside an interactive Terminal view.
/// With entity-based BUIs, the remote computer and its running program entities deliver their own
/// BUI states directly to the authorized remote viewer client and render inside RemoteScreenView.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteScreenState
{
    public NetEntity TargetComputerNetEntity { get; }
    public NetEntity? HostMachineNetEntity { get; }
    public Enum PrimaryUiKey { get; }
    public string DeviceGuid { get; }
    public string DeviceName { get; }
    public string DeviceCategory { get; }
    public string ForegroundProgramTitle { get; }
    public bool AllowRemoteControl { get; }
    public bool IsBooted { get; }
    public List<NetEntity> ProgramEntities { get; }
    public Vector2i ScreenSize { get; }
    public int Depth { get; }
    public string? ViewingTargetGuid { get; }
    public bool IsCycleReference { get; }
    public bool WasAlreadyOpen { get; }
    public BoundUserInterfaceState? BuiState { get; }

    public NetEntity TargetEntity => TargetComputerNetEntity;
    public Enum? UiKey => PrimaryUiKey;

    public RemoteScreenState(
        NetEntity targetComputerNetEntity,
        NetEntity? hostMachineNetEntity,
        Enum primaryUiKey,
        string deviceGuid,
        string deviceName,
        string deviceCategory,
        string foregroundProgramTitle,
        bool allowRemoteControl,
        bool isBooted,
        List<NetEntity> programEntities,
        Vector2i screenSize,
        int depth = 0,
        string? viewingTargetGuid = null,
        bool isCycleReference = false,
        bool wasAlreadyOpen = false,
        BoundUserInterfaceState? buiState = null)
    {
        TargetComputerNetEntity = targetComputerNetEntity;
        HostMachineNetEntity = hostMachineNetEntity;
        PrimaryUiKey = primaryUiKey;
        DeviceGuid = deviceGuid;
        DeviceName = deviceName;
        DeviceCategory = deviceCategory;
        ForegroundProgramTitle = foregroundProgramTitle;
        AllowRemoteControl = allowRemoteControl;
        IsBooted = isBooted;
        ProgramEntities = programEntities;
        ScreenSize = screenSize;
        Depth = depth;
        ViewingTargetGuid = viewingTargetGuid;
        IsCycleReference = isCycleReference;
        WasAlreadyOpen = wasAlreadyOpen;
        BuiState = buiState;
    }
}
