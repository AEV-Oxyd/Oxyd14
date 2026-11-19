using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Telemetry snapshot populated strictly from DeviceLink AirAlarmStatus packets.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AtmosControlProgramComponent : Component
{
    [DataField]
    public bool HasTelemetry;

    [DataField]
    public string AlarmLevel = "Normal";

    [DataField]
    public string CurrentMode = "Filtering";

    [DataField]
    public bool AutoMode = true;

    [DataField]
    public bool Locked = true;

    [DataField]
    public float PressureKpa;

    [DataField]
    public float TemperatureCelsius;

    [DataField]
    public int VentCount;

    [DataField]
    public int ScrubberCount;

    [DataField]
    public int SensorCount;

    [DataField]
    public string GasSummary = "No atmospheric telemetry";

    [DataField]
    public string StatusMessage = "Awaiting DeviceLink telemetry from Air Alarm...";
}

[Serializable, NetSerializable]
public sealed class AtmosControlUiState : BoundUserInterfaceState
{
    public bool HasTelemetry { get; }
    public bool IsDeviceLinkEnabled { get; }
    public string AlarmLevel { get; }
    public string CurrentMode { get; }
    public bool AutoMode { get; }
    public bool Locked { get; }
    public float PressureKpa { get; }
    public float TemperatureCelsius { get; }
    public int VentCount { get; }
    public int ScrubberCount { get; }
    public int SensorCount { get; }
    public string GasSummary { get; }
    public string StatusMessage { get; }

    public AtmosControlUiState(
        bool hasTelemetry,
        bool isDeviceLinkEnabled,
        string alarmLevel,
        string currentMode,
        bool autoMode,
        bool locked,
        float pressureKpa,
        float temperatureCelsius,
        int ventCount,
        int scrubberCount,
        int sensorCount,
        string gasSummary,
        string statusMessage)
    {
        HasTelemetry = hasTelemetry;
        IsDeviceLinkEnabled = isDeviceLinkEnabled;
        AlarmLevel = alarmLevel;
        CurrentMode = currentMode;
        AutoMode = autoMode;
        Locked = locked;
        PressureKpa = pressureKpa;
        TemperatureCelsius = temperatureCelsius;
        VentCount = ventCount;
        ScrubberCount = scrubberCount;
        SensorCount = sensorCount;
        GasSummary = gasSummary;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public enum AtmosControlAction : byte
{
    Refresh,
    SetModeFiltering,
    SetModeWideFiltering,
    SetModeFill,
    SetModePanic,
    SetModeNone,
    ToggleAutoMode,
    ResyncDevices,
    SaveLog
}

[Serializable, NetSerializable]
public sealed class AtmosControlActionMessage : BoundUserInterfaceMessage
{
    public AtmosControlAction Action { get; }

    public AtmosControlActionMessage(AtmosControlAction action)
    {
        Action = action;
    }
}
