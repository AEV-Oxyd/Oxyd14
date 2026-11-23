using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Telemetry snapshot populated strictly from DeviceLink ApcStatus packets.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ApcControlProgramComponent : Component
{
    [DataField]
    public bool HasTelemetry;

    [DataField]
    public string AreaName = "Power Controller";

    [DataField]
    public bool MainBreakerEnabled;

    [DataField]
    public bool BreakerTripped;

    [DataField]
    public bool Locked = true;

    [DataField]
    public float ChargePercent;

    [DataField]
    public int LoadWatts;

    [DataField]
    public string ExternalPowerState = "None";

    [DataField]
    public string StatusMessage = "Awaiting DeviceLink telemetry from APC...";

    [DataField]
    public string? SelectedTargetGuid;

    [DataField]
    public string? SelectedAccFilePath;

    [DataField]
    public HashSet<string> AuthorizedTargetGuids = new();
}

[Serializable, NetSerializable]
public sealed class RemoteApcTargetDto
{
    public string TargetGuid { get; }
    public string AreaName { get; }
    public bool IsAuthorized { get; }
    public bool MainBreakerEnabled { get; }
    public float ChargePercent { get; }
    public int LoadWatts { get; }

    public RemoteApcTargetDto(
        string targetGuid,
        string areaName,
        bool isAuthorized,
        bool mainBreakerEnabled,
        float chargePercent,
        int loadWatts)
    {
        TargetGuid = targetGuid;
        AreaName = areaName;
        IsAuthorized = isAuthorized;
        MainBreakerEnabled = mainBreakerEnabled;
        ChargePercent = chargePercent;
        LoadWatts = loadWatts;
    }
}

[Serializable, NetSerializable]
public sealed class ApcControlUiState : BoundUserInterfaceState
{
    public bool HasTelemetry { get; }
    public bool IsDeviceLinkEnabled { get; }
    public string AreaName { get; }
    public bool MainBreakerEnabled { get; }
    public bool BreakerTripped { get; }
    public bool Locked { get; }
    public float ChargePercent { get; }
    public int LoadWatts { get; }
    public string ExternalPowerState { get; }
    public string StatusMessage { get; }
    public string SelectedTargetGuid { get; }
    public bool IsTargetAuthorized { get; }
    public bool IsRemoteConsole { get; }
    public string? SelectedAccFilePath { get; }
    public List<string> AvailableAccFiles { get; }
    public List<RemoteApcTargetDto> AvailableTargets { get; }

    public ApcControlUiState(
        bool hasTelemetry,
        bool isDeviceLinkEnabled,
        string areaName,
        bool mainBreakerEnabled,
        bool breakerTripped,
        bool locked,
        float chargePercent,
        int loadWatts,
        string externalPowerState,
        string statusMessage,
        string selectedTargetGuid = "",
        bool isTargetAuthorized = false,
        string? selectedAccFilePath = null,
        List<string>? availableAccFiles = null,
        List<RemoteApcTargetDto>? availableTargets = null,
        bool isRemoteConsole = false)
    {
        HasTelemetry = hasTelemetry;
        IsDeviceLinkEnabled = isDeviceLinkEnabled;
        AreaName = areaName;
        MainBreakerEnabled = mainBreakerEnabled;
        BreakerTripped = breakerTripped;
        Locked = locked;
        ChargePercent = chargePercent;
        LoadWatts = loadWatts;
        ExternalPowerState = externalPowerState;
        StatusMessage = statusMessage;
        SelectedTargetGuid = selectedTargetGuid;
        IsTargetAuthorized = isTargetAuthorized;
        IsRemoteConsole = isRemoteConsole;
        SelectedAccFilePath = selectedAccFilePath;
        AvailableAccFiles = availableAccFiles ?? new List<string>();
        AvailableTargets = availableTargets ?? new List<RemoteApcTargetDto>();
    }
}

[Serializable, NetSerializable]
public enum ApcControlAction : byte
{
    Refresh,
    ToggleBreaker,
    SetBreakerOn,
    SetBreakerOff,
    SaveLog,
    SelectTarget,
    SelectAccFile,
    AuthorizeWithAcc,
    RevokeAuth
}

[Serializable, NetSerializable]
public sealed class ApcControlActionMessage : BoundUserInterfaceMessage
{
    public ApcControlAction Action { get; }
    public string Argument { get; }

    public ApcControlActionMessage(ApcControlAction action, string argument = "")
    {
        Action = action;
        Argument = argument;
    }
}
