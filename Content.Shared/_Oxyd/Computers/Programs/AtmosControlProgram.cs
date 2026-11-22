using System.Globalization;
using System.Linq;
using System.Text;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.DeviceNetwork;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Telemetry and linked-device state populated strictly from DeviceLink AirAlarmStatus packets,
/// plus in-app reusable device templates.
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

    [ViewVariables]
    public List<AtmosVentEntryDto> Vents = new();

    [ViewVariables]
    public List<AtmosScrubberEntryDto> Scrubbers = new();

    [ViewVariables]
    public List<AtmosSensorEntryDto> Sensors = new();

    [ViewVariables]
    public List<AtmosTemplateEntryDto> Templates = new();
}

[Serializable, NetSerializable]
public sealed class AtmosVentEntryDto
{
    public int AddressId { get; }
    public string DisplayAddress { get; }
    public bool Enabled { get; }
    public VentPumpDirection PumpDirection { get; }
    public VentPressureBound PressureChecks { get; }
    public float ExternalPressureBound { get; }
    public float InternalPressureBound { get; }

    public AtmosVentEntryDto(
        int addressId,
        string displayAddress,
        bool enabled,
        VentPumpDirection pumpDirection,
        VentPressureBound pressureChecks,
        float externalPressureBound,
        float internalPressureBound)
    {
        AddressId = addressId;
        DisplayAddress = displayAddress;
        Enabled = enabled;
        PumpDirection = pumpDirection;
        PressureChecks = pressureChecks;
        ExternalPressureBound = externalPressureBound;
        InternalPressureBound = internalPressureBound;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosScrubberEntryDto
{
    public int AddressId { get; }
    public string DisplayAddress { get; }
    public bool Enabled { get; }
    public ScrubberPumpDirection PumpDirection { get; }
    public float VolumeRate { get; }
    public bool WideNet { get; }
    public bool PanicWireCut { get; }
    public List<Gas> FilterGases { get; }

    public AtmosScrubberEntryDto(
        int addressId,
        string displayAddress,
        bool enabled,
        ScrubberPumpDirection pumpDirection,
        float volumeRate,
        bool wideNet,
        bool panicWireCut,
        List<Gas> filterGases)
    {
        AddressId = addressId;
        DisplayAddress = displayAddress;
        Enabled = enabled;
        PumpDirection = pumpDirection;
        VolumeRate = volumeRate;
        WideNet = wideNet;
        PanicWireCut = panicWireCut;
        FilterGases = filterGases;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosThresholdDto
{
    public bool Ignore { get; }
    public bool LowerDangerEnabled { get; }
    public float LowerDanger { get; }
    public bool LowerWarningEnabled { get; }
    public float LowerWarning { get; }
    public bool UpperWarningEnabled { get; }
    public float UpperWarning { get; }
    public bool UpperDangerEnabled { get; }
    public float UpperDanger { get; }

    public AtmosThresholdDto(
        bool ignore,
        bool lowerDangerEnabled,
        float lowerDanger,
        bool lowerWarningEnabled,
        float lowerWarning,
        bool upperWarningEnabled,
        float upperWarning,
        bool upperDangerEnabled,
        float upperDanger)
    {
        Ignore = ignore;
        LowerDangerEnabled = lowerDangerEnabled;
        LowerDanger = lowerDanger;
        LowerWarningEnabled = lowerWarningEnabled;
        LowerWarning = lowerWarning;
        UpperWarningEnabled = upperWarningEnabled;
        UpperWarning = upperWarning;
        UpperDangerEnabled = upperDangerEnabled;
        UpperDanger = upperDanger;
    }

    public static AtmosThresholdDto FromThreshold(AtmosAlarmThreshold? t)
    {
        if (t == null)
            return new AtmosThresholdDto(true, false, 0f, false, 0f, false, 0f, false, 0f);

        var lw = t.LowerWarningBound;
        var uw = t.UpperWarningBound;
        return new AtmosThresholdDto(
            t.Ignore,
            t.LowerBound.Enabled,
            t.LowerBound.Value,
            lw.Enabled,
            lw.Value,
            uw.Enabled,
            uw.Value,
            t.UpperBound.Enabled,
            t.UpperBound.Value);
    }

    public AtmosAlarmThreshold ToThreshold()
    {
        var t = new AtmosAlarmThreshold { Ignore = Ignore };
        var ld = LowerDanger > 0f ? LowerDanger : (LowerWarning > 0f ? LowerWarning : 0.001f);
        var ud = UpperDanger > 0f ? UpperDanger : (UpperWarning > 0f ? UpperWarning : 0.001f);
        if (ud < ld)
            ud = ld;

        t.LowerBound = new AlarmThresholdSetting { Enabled = LowerDangerEnabled, Value = ld };
        t.UpperBound = new AlarmThresholdSetting { Enabled = UpperDangerEnabled, Value = ud };
        t.LowerWarningBound = new AlarmThresholdSetting { Enabled = LowerWarningEnabled, Value = LowerWarning > 0f ? LowerWarning : ld };
        t.UpperWarningBound = new AlarmThresholdSetting { Enabled = UpperWarningEnabled, Value = UpperWarning > 0f ? UpperWarning : ud };
        t.SetEnabled(AtmosMonitorLimitType.LowerDanger, LowerDangerEnabled);
        t.SetEnabled(AtmosMonitorLimitType.LowerWarning, LowerWarningEnabled);
        t.SetEnabled(AtmosMonitorLimitType.UpperWarning, UpperWarningEnabled);
        t.SetEnabled(AtmosMonitorLimitType.UpperDanger, UpperDangerEnabled);
        return t;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosSensorEntryDto
{
    public int AddressId { get; }
    public string DisplayAddress { get; }
    public AtmosAlarmType AlarmState { get; }
    public float Pressure { get; }
    public float Temperature { get; }
    public float TotalMoles { get; }
    public Dictionary<Gas, float> Gases { get; }
    public AtmosThresholdDto PressureThreshold { get; }
    public AtmosThresholdDto TemperatureThreshold { get; }
    public Dictionary<Gas, AtmosThresholdDto> GasThresholds { get; }

    public AtmosSensorEntryDto(
        int addressId,
        string displayAddress,
        AtmosAlarmType alarmState,
        float pressure,
        float temperature,
        float totalMoles,
        Dictionary<Gas, float> gases,
        AtmosThresholdDto pressureThreshold,
        AtmosThresholdDto temperatureThreshold,
        Dictionary<Gas, AtmosThresholdDto> gasThresholds)
    {
        AddressId = addressId;
        DisplayAddress = displayAddress;
        AlarmState = alarmState;
        Pressure = pressure;
        Temperature = temperature;
        TotalMoles = totalMoles;
        Gases = gases;
        PressureThreshold = pressureThreshold;
        TemperatureThreshold = temperatureThreshold;
        GasThresholds = gasThresholds;
    }
}

[Serializable, NetSerializable]
public enum AtmosTemplateKind : byte
{
    Vent = 0,
    Scrubber = 1,
    Sensor = 2
}

[Serializable, NetSerializable]
public sealed class AtmosSensorTemplateDto
{
    public AtmosThresholdDto PressureThreshold { get; }
    public AtmosThresholdDto TemperatureThreshold { get; }
    public Dictionary<Gas, AtmosThresholdDto> GasThresholds { get; }

    public AtmosSensorTemplateDto(
        AtmosThresholdDto pressureThreshold,
        AtmosThresholdDto temperatureThreshold,
        Dictionary<Gas, AtmosThresholdDto> gasThresholds)
    {
        PressureThreshold = pressureThreshold;
        TemperatureThreshold = temperatureThreshold;
        GasThresholds = gasThresholds;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosTemplateEntryDto
{
    public string Name { get; }
    public AtmosTemplateKind Kind { get; }
    public AtmosVentEntryDto? Vent { get; }
    public AtmosScrubberEntryDto? Scrubber { get; }
    public AtmosSensorTemplateDto? Sensor { get; }

    public AtmosTemplateEntryDto(
        string name,
        AtmosTemplateKind kind,
        AtmosVentEntryDto? vent = null,
        AtmosScrubberEntryDto? scrubber = null,
        AtmosSensorTemplateDto? sensor = null)
    {
        Name = name;
        Kind = kind;
        Vent = vent;
        Scrubber = scrubber;
        Sensor = sensor;
    }
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
    public List<AtmosVentEntryDto> Vents { get; }
    public List<AtmosScrubberEntryDto> Scrubbers { get; }
    public List<AtmosSensorEntryDto> Sensors { get; }
    public List<AtmosTemplateEntryDto> Templates { get; }

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
        string statusMessage,
        List<AtmosVentEntryDto>? vents = null,
        List<AtmosScrubberEntryDto>? scrubbers = null,
        List<AtmosSensorEntryDto>? sensors = null,
        List<AtmosTemplateEntryDto>? templates = null)
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
        Vents = vents ?? new List<AtmosVentEntryDto>();
        Scrubbers = scrubbers ?? new List<AtmosScrubberEntryDto>();
        Sensors = sensors ?? new List<AtmosSensorEntryDto>();
        Templates = templates ?? new List<AtmosTemplateEntryDto>();
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

[Serializable, NetSerializable]
public sealed class AtmosUpdateVentMessage : BoundUserInterfaceMessage
{
    public int AddressId { get; }
    public bool Enabled { get; }
    public VentPumpDirection PumpDirection { get; }
    public VentPressureBound PressureChecks { get; }
    public float ExternalPressureBound { get; }
    public float InternalPressureBound { get; }
    public bool ApplyToAll { get; }

    public AtmosUpdateVentMessage(
        int addressId,
        bool enabled,
        VentPumpDirection pumpDirection,
        VentPressureBound pressureChecks,
        float externalPressureBound,
        float internalPressureBound,
        bool applyToAll = false)
    {
        AddressId = addressId;
        Enabled = enabled;
        PumpDirection = pumpDirection;
        PressureChecks = pressureChecks;
        ExternalPressureBound = externalPressureBound;
        InternalPressureBound = internalPressureBound;
        ApplyToAll = applyToAll;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosUpdateScrubberMessage : BoundUserInterfaceMessage
{
    public int AddressId { get; }
    public bool Enabled { get; }
    public ScrubberPumpDirection PumpDirection { get; }
    public float VolumeRate { get; }
    public bool WideNet { get; }
    public List<Gas> FilterGases { get; }
    public bool ApplyToAll { get; }

    public AtmosUpdateScrubberMessage(
        int addressId,
        bool enabled,
        ScrubberPumpDirection pumpDirection,
        float volumeRate,
        bool wideNet,
        List<Gas> filterGases,
        bool applyToAll = false)
    {
        AddressId = addressId;
        Enabled = enabled;
        PumpDirection = pumpDirection;
        VolumeRate = volumeRate;
        WideNet = wideNet;
        FilterGases = filterGases;
        ApplyToAll = applyToAll;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosUpdateSensorThresholdMessage : BoundUserInterfaceMessage
{
    public int AddressId { get; }
    public AtmosMonitorThresholdType ThresholdType { get; }
    public Gas? Gas { get; }
    public AtmosThresholdDto Threshold { get; }
    public bool ApplyToAll { get; }

    public AtmosUpdateSensorThresholdMessage(
        int addressId,
        AtmosMonitorThresholdType thresholdType,
        AtmosThresholdDto threshold,
        Gas? gas = null,
        bool applyToAll = false)
    {
        AddressId = addressId;
        ThresholdType = thresholdType;
        Threshold = threshold;
        Gas = gas;
        ApplyToAll = applyToAll;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosSaveTemplateMessage : BoundUserInterfaceMessage
{
    public string Name { get; }
    public AtmosTemplateKind Kind { get; }
    public AtmosVentEntryDto? Vent { get; }
    public AtmosScrubberEntryDto? Scrubber { get; }
    public AtmosSensorTemplateDto? Sensor { get; }

    public AtmosSaveTemplateMessage(
        string name,
        AtmosTemplateKind kind,
        AtmosVentEntryDto? vent = null,
        AtmosScrubberEntryDto? scrubber = null,
        AtmosSensorTemplateDto? sensor = null)
    {
        Name = name;
        Kind = kind;
        Vent = vent;
        Scrubber = scrubber;
        Sensor = sensor;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosDeleteTemplateMessage : BoundUserInterfaceMessage
{
    public string Name { get; }
    public AtmosTemplateKind Kind { get; }

    public AtmosDeleteTemplateMessage(string name, AtmosTemplateKind kind)
    {
        Name = name;
        Kind = kind;
    }
}

[Serializable, NetSerializable]
public sealed class AtmosApplyTemplateMessage : BoundUserInterfaceMessage
{
    public string Name { get; }
    public AtmosTemplateKind Kind { get; }
    public int? TargetAddressId { get; }
    public bool ApplyToAll { get; }

    public AtmosApplyTemplateMessage(
        string name,
        AtmosTemplateKind kind,
        int? targetAddressId = null,
        bool applyToAll = false)
    {
        Name = name;
        Kind = kind;
        TargetAddressId = targetAddressId;
        ApplyToAll = applyToAll;
    }
}

/// <summary>
/// Deterministic, reflection-free packet encoding/decoding for linked Atmos devices over DeviceLink ComputerPackets.
/// </summary>
public static class AtmosControlCodec
{
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static float PF(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    private static int PI(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static string CleanLabel(string s) => s.Replace("|", "/").Replace(";", ",").Replace("\n", " ").Replace("\r", "");

    public static string EncodeThreshold(AtmosThresholdDto t)
    {
        var sb = new StringBuilder(64);
        sb.Append(t.Ignore ? '1' : '0').Append(',')
            .Append(t.LowerDangerEnabled ? '1' : '0').Append(',').Append(F(t.LowerDanger)).Append(',')
            .Append(t.LowerWarningEnabled ? '1' : '0').Append(',').Append(F(t.LowerWarning)).Append(',')
            .Append(t.UpperWarningEnabled ? '1' : '0').Append(',').Append(F(t.UpperWarning)).Append(',')
            .Append(t.UpperDangerEnabled ? '1' : '0').Append(',').Append(F(t.UpperDanger));
        return sb.ToString();
    }

    public static AtmosThresholdDto DecodeThreshold(string raw)
    {
        var p = raw.Split(',');
        if (p.Length < 9)
            return new AtmosThresholdDto(true, false, 0f, false, 0f, false, 0f, false, 0f);

        return new AtmosThresholdDto(
            p[0] == "1",
            p[1] == "1", PF(p[2]),
            p[3] == "1", PF(p[4]),
            p[5] == "1", PF(p[6]),
            p[7] == "1", PF(p[8]));
    }

    public static string EncodeVents(IEnumerable<AtmosVentEntryDto> vents)
    {
        var sb = new StringBuilder();
        foreach (var v in vents)
        {
            if (sb.Length > 0)
                sb.Append(';');
            sb.Append(v.AddressId).Append('|')
                .Append(CleanLabel(v.DisplayAddress)).Append('|')
                .Append(v.Enabled ? '1' : '0').Append('|')
                .Append((int) v.PumpDirection).Append('|')
                .Append((int) v.PressureChecks).Append('|')
                .Append(F(v.ExternalPressureBound)).Append('|')
                .Append(F(v.InternalPressureBound));
        }
        return sb.ToString();
    }

    public static List<AtmosVentEntryDto> DecodeVents(string raw)
    {
        var list = new List<AtmosVentEntryDto>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = item.Split('|');
            if (p.Length < 7)
                continue;
            list.Add(new AtmosVentEntryDto(
                PI(p[0]),
                p[1],
                p[2] == "1",
                (VentPumpDirection) PI(p[3]),
                (VentPressureBound) PI(p[4]),
                PF(p[5]),
                PF(p[6])));
        }
        return list;
    }

    public static string EncodeScrubbers(IEnumerable<AtmosScrubberEntryDto> scrubbers)
    {
        var sb = new StringBuilder();
        foreach (var s in scrubbers)
        {
            if (sb.Length > 0)
                sb.Append(';');
            var gases = string.Join(",", s.FilterGases.Select(g => ((int) g).ToString(CultureInfo.InvariantCulture)));
            sb.Append(s.AddressId).Append('|')
                .Append(CleanLabel(s.DisplayAddress)).Append('|')
                .Append(s.Enabled ? '1' : '0').Append('|')
                .Append((int) s.PumpDirection).Append('|')
                .Append(F(s.VolumeRate)).Append('|')
                .Append(s.WideNet ? '1' : '0').Append('|')
                .Append(s.PanicWireCut ? '1' : '0').Append('|')
                .Append(gases);
        }
        return sb.ToString();
    }

    public static List<AtmosScrubberEntryDto> DecodeScrubbers(string raw)
    {
        var list = new List<AtmosScrubberEntryDto>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = item.Split('|');
            if (p.Length < 8)
                continue;

            var gases = new List<Gas>();
            if (!string.IsNullOrWhiteSpace(p[7]))
            {
                foreach (var gStr in p[7].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    gases.Add((Gas) PI(gStr));
            }

            list.Add(new AtmosScrubberEntryDto(
                PI(p[0]),
                p[1],
                p[2] == "1",
                (ScrubberPumpDirection) PI(p[3]),
                PF(p[4]),
                p[5] == "1",
                p[6] == "1",
                gases));
        }
        return list;
    }

    public static string EncodeSensors(IEnumerable<AtmosSensorEntryDto> sensors)
    {
        var sb = new StringBuilder();
        foreach (var s in sensors)
        {
            if (sb.Length > 0)
                sb.Append(';');

            var gasAmounts = string.Join(",", s.Gases.Select(kv => $"{(int) kv.Key}={F(kv.Value)}"));
            var gasThresholds = string.Join("~", s.GasThresholds.Select(kv => $"{(int) kv.Key}={EncodeThreshold(kv.Value)}"));

            sb.Append(s.AddressId).Append('|')
                .Append(CleanLabel(s.DisplayAddress)).Append('|')
                .Append((int) s.AlarmState).Append('|')
                .Append(F(s.Pressure)).Append('|')
                .Append(F(s.Temperature)).Append('|')
                .Append(F(s.TotalMoles)).Append('|')
                .Append(EncodeThreshold(s.PressureThreshold)).Append('|')
                .Append(EncodeThreshold(s.TemperatureThreshold)).Append('|')
                .Append(gasAmounts).Append('|')
                .Append(gasThresholds);
        }
        return sb.ToString();
    }

    public static List<AtmosSensorEntryDto> DecodeSensors(string raw)
    {
        var list = new List<AtmosSensorEntryDto>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;

        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = item.Split('|');
            if (p.Length < 10)
                continue;

            var gases = new Dictionary<Gas, float>();
            if (!string.IsNullOrWhiteSpace(p[8]))
            {
                foreach (var kvRaw in p[8].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = kvRaw.Split('=', 2);
                    if (kv.Length == 2)
                        gases[(Gas) PI(kv[0])] = PF(kv[1]);
                }
            }

            var gasThresholds = new Dictionary<Gas, AtmosThresholdDto>();
            if (!string.IsNullOrWhiteSpace(p[9]))
            {
                foreach (var kvRaw in p[9].Split('~', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = kvRaw.Split('=', 2);
                    if (kv.Length == 2)
                        gasThresholds[(Gas) PI(kv[0])] = DecodeThreshold(kv[1]);
                }
            }

            list.Add(new AtmosSensorEntryDto(
                PI(p[0]),
                p[1],
                (AtmosAlarmType) PI(p[2]),
                PF(p[3]),
                PF(p[4]),
                PF(p[5]),
                gases,
                DecodeThreshold(p[6]),
                DecodeThreshold(p[7]),
                gasThresholds));
        }
        return list;
    }
}
