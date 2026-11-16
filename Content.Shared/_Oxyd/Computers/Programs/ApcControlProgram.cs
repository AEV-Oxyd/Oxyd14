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
    public bool Locked;

    [DataField]
    public float ChargePercent;

    [DataField]
    public int LoadWatts;

    [DataField]
    public string ExternalPowerState = "None";

    [DataField]
    public string StatusMessage = "Awaiting DeviceLink telemetry from APC...";
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
        string statusMessage)
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
    }
}

[Serializable, NetSerializable]
public enum ApcControlAction : byte
{
    Refresh,
    ToggleBreaker,
    SetBreakerOn,
    SetBreakerOff,
    SaveLog
}

[Serializable, NetSerializable]
public sealed class ApcControlActionMessage : BoundUserInterfaceMessage
{
    public ApcControlAction Action { get; }

    public ApcControlActionMessage(ApcControlAction action)
    {
        Action = action;
    }
}
