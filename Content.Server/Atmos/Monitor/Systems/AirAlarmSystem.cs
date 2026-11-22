using Content.Server.Atmos.Monitor.Components;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork.Systems;
using Content.Server.Popups;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Database;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using System.Linq;
using Content.Server.Atmos.Monitor.Payloads;
using Content.Shared.Atmos.Components;
using Content.Shared.DeviceConfigurator;
using Content.Shared.DeviceConfigurator.Systems;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Lock;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Server.Atmos.Monitor.Systems;

// AirAlarm system - specific for atmos devices, rather than
// atmos monitors.
public sealed partial class AirAlarmSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private AtmosAlarmableSystem _atmosAlarmable = default!;
    [Dependency] private AtmosDeviceNetworkSystem _atmosDevNet = default!;
    [Dependency] private DeviceNetworkSystem _deviceNet = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private DeviceListSystem _deviceList = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    [Dependency] private EntityQuery<DeviceNetworkComponent> _deviceNetworkQuery = default!;

    private static readonly ProtoId<SourcePortPrototype> PortAirAlarmStatusSource = "AirAlarmStatusSource";
    private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";

    #region Device Network API

    // -- API --

    public void SetData(EntityUid uid, DeviceAddress address, GasVentPumpData payload)
    {
        var sendPayload = new GasVentPumpSetDataPayload { Data = payload };
        _deviceNet.SendPacket(uid, address, ref sendPayload);
        SetDeviceDataInternal(uid, address, payload);
    }

    public void SetData(EntityUid uid, DeviceAddress address, GasVentScrubberData payload)
    {
        var sendPayload = new GasVentScrubberSetDataPayload { Data = payload };
        _deviceNet.SendPacket(uid, address, ref sendPayload);
        SetDeviceDataInternal(uid, address, payload);
    }

    private void SetDeviceDataInternal(EntityUid uid, DeviceAddress address, IAtmosDeviceData payload)
    {
        var setPayload = new AirAlarmSetDataPayload { Payload = payload };
        _deviceNet.SendPacket(uid, address, ref setPayload);
    }

    /// <summary>
    ///     Broadcast a sync packet to an air alarm's local network.
    /// </summary>
    private void SyncAllDevices(EntityUid uid)
    {
        _atmosDevNet.Sync(uid, null);
    }

    /// <summary>
    ///     Send a sync packet to a specific device from an air alarm.
    /// </summary>
    /// <param name="address">The address of the device.</param>
    private void SyncDevice(EntityUid uid, DeviceAddress address)
    {
        _atmosDevNet.Sync(uid, address);
    }

    /// <summary>
    ///     Register and synchronize with all devices
    ///     on this network.
    /// </summary>
    /// <param name="uid"></param>
    private void SyncRegisterAllDevices(EntityUid uid)
    {
        _atmosDevNet.Register(uid, null);
        _atmosDevNet.Sync(uid, null);
    }

    /// <summary>
    ///     Synchronize all sensors on an air alarm, but only if its current tab is set to Sensors.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="monitor"></param>
    private void SyncAllSensors(EntityUid uid, AirAlarmComponent? monitor = null)
    {
        if (!Resolve(uid, ref monitor))
        {
            return;
        }

        foreach (var addr in monitor.SensorData.Keys)
        {
            SyncDevice(uid, addr.AddressId);
        }
    }

    private void SetThreshold(EntityUid uid, DeviceAddress address, AtmosMonitorThresholdType type,
        AtmosAlarmThreshold threshold, Gas? gas = null)
    {
        var payload = new AtmosMonitorSetThresholdPayload
        {
            Type = type,
            Threshold = threshold,
            Gas = gas,
        };

        _deviceNet.SendPacket(uid, address, ref payload);

        SyncDevice(uid, address);
    }

    private void SetAllThresholds(EntityUid uid, DeviceAddress address, AtmosMonitorData dataPayload)
    {
        var payload = new AtmosMonitorSetAllThresholdsPayload
        {
            Data = dataPayload
        };

        _deviceNet.SendPacket(uid, address, ref payload);

        SyncDevice(uid, address);
    }

    /// <summary>
    ///     Sync this air alarm's mode with the rest of the network.
    /// </summary>
    /// <param name="mode">The mode to sync with the rest of the network.</param>
    private void SyncMode(EntityUid uid, AirAlarmMode mode)
    {
        if (TryComp<AtmosMonitorComponent>(uid, out var monitor) && !monitor.NetEnabled)
            return;

        var payload = new AirAlarmSetModePayload
        {
            Mode = mode,
        };

        _deviceNet.SendPacket(uid, null, ref payload);
    }

    #endregion

    #region Events

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AirAlarmComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
        SubscribeLocalEvent<AirAlarmComponent, AtmosAlarmEvent>(OnAtmosAlarm);
        SubscribeLocalEvent<AirAlarmComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<AirAlarmComponent, DeviceListUpdateEvent>(OnDeviceListUpdate);
        SubscribeLocalEvent<AirAlarmComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AirAlarmComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AirAlarmComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AirAlarmComponent, AfterActivatableUIOpenEvent>(OnActivate);
        SubscribeLocalEvent<AirAlarmComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnComputerDeviceLinkSignal);
        SubscribeLocalEvent<AirAlarmComponent, SignalReceivedEvent<ComputerPacket>>(OnComputerPacketSignal);

        Subs.BuiEvents<AirAlarmComponent>(SharedAirAlarmInterfaceKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnClose);
            subs.Event<AirAlarmResyncAllDevicesMessage>(OnResyncAll);
            subs.Event<AirAlarmUpdateAlarmModeMessage>(OnUpdateAlarmMode);
            subs.Event<AirAlarmUpdateAutoModeMessage>(OnUpdateAutoMode);
            subs.Event<AirAlarmUpdateAlarmThresholdMessage>(OnUpdateThreshold);
            subs.Event<AirAlarmUpdateDeviceDataMessage>(OnUpdateDeviceData);
            subs.Event<AirAlarmCopyDeviceDataMessage>(OnCopyDeviceData);
        });
    }

    private void OnComputerPacketSignal(Entity<AirAlarmComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        EntityUid? actor = args.Data.ProtectedReferences.TryGetValue("Actor", out var actUid) ? actUid : args.Trigger;
        HandleAirAlarmDeviceLinkCommand(ent, args.Data.PacketType, args.Data.GetString("Argument", string.Empty) ?? string.Empty, actor, args.Data);
    }

    private void OnComputerDeviceLinkSignal(Entity<AirAlarmComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        HandleAirAlarmDeviceLinkCommand(ent, args.Data.Command, args.Data.Argument, args.Trigger, null);
    }

    private void HandleAirAlarmDeviceLinkCommand(Entity<AirAlarmComponent> ent, string? rawCmd, string rawArg, EntityUid? actor, ComputerPacket? packet)
    {
        var cmd = rawCmd?.Trim().ToLowerInvariant() ?? string.Empty;
        if (cmd is "status" or "status_request")
        {
            SyncAllDevices(ent.Owner);
            EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
            return;
        }

        if (_lock.IsLocked(ent.Owner))
        {
            EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
            return;
        }

        if (cmd is "atmos_set_mode" or "set_mode" or "mode")
        {
            var modeStr = rawArg.Trim().ToLowerInvariant();
            var mode = modeStr switch
            {
                "filtering" => AirAlarmMode.Filtering,
                "widefiltering" => AirAlarmMode.WideFiltering,
                "fill" => AirAlarmMode.Fill,
                "panic" => AirAlarmMode.Panic,
                "none" or "off" => AirAlarmMode.None,
                _ => ent.Comp.CurrentMode
            };

            var addr = TryComp<DeviceNetworkComponent>(ent.Owner, out var netConn) ? netConn.Address : DeviceAddress.Invalid;
            SetMode(ent.Owner, addr, mode, false, ent.Comp);
            EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
        }
        else if (cmd == "set_auto_mode")
        {
            var wantAuto = rawArg.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            ent.Comp.AutoMode = wantAuto;
            UpdateUI(ent.Owner, ent.Comp);
            EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
        }
        else if (cmd == "resync_devices")
        {
            ent.Comp.KnownDevices.Clear();
            ent.Comp.VentData.Clear();
            ent.Comp.ScrubberData.Clear();
            ent.Comp.SensorData.Clear();
            SyncRegisterAllDevices(ent.Owner);
            EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
        }
        else if (cmd is "set_vent" or "set_all_vents" && packet != null)
        {
            var decoded = AtmosControlCodec.DecodeVents(packet.GetString("VentRaw", string.Empty) ?? string.Empty);
            if (decoded.Count == 0)
                return;

            var src = decoded[0];
            var ventData = new GasVentPumpData
            {
                Enabled = src.Enabled,
                PumpDirection = src.PumpDirection,
                PressureChecks = src.PressureChecks,
                ExternalPressureBound = MathF.Max(0f, src.ExternalPressureBound),
                InternalPressureBound = MathF.Max(0f, src.InternalPressureBound),
                Dirty = true
            };

            if (cmd == "set_all_vents")
            {
                foreach (var locAddr in ent.Comp.VentData.Keys.ToList())
                {
                    var clone = new GasVentPumpData
                    {
                        Enabled = ventData.Enabled,
                        PumpDirection = ventData.PumpDirection,
                        PressureChecks = ventData.PressureChecks,
                        ExternalPressureBound = ventData.ExternalPressureBound,
                        InternalPressureBound = ventData.InternalPressureBound,
                        Dirty = true
                    };
                    ent.Comp.VentData[locAddr] = clone;
                    SetData(ent.Owner, locAddr.AddressId, clone);
                }
            }
            else
            {
                var targetAddr = new DeviceAddress(src.AddressId);
                if (_deviceList.ExistsInDeviceList(ent.Owner, targetAddr) ||
                    ent.Comp.VentData.Keys.Any(k => k.AddressId == targetAddr))
                {
                    foreach (var locAddr in ent.Comp.VentData.Keys.ToList())
                    {
                        if (locAddr.AddressId == targetAddr)
                            ent.Comp.VentData[locAddr] = ventData;
                    }
                    SetDeviceData(ent.Owner, targetAddr, ventData, ent.Comp);
                }
            }

            UpdateUI(ent.Owner, ent.Comp);
        }
        else if (cmd is "set_scrubber" or "set_all_scrubbers" && packet != null)
        {
            var decoded = AtmosControlCodec.DecodeScrubbers(packet.GetString("ScrubberRaw", string.Empty) ?? string.Empty);
            if (decoded.Count == 0)
                return;

            var src = decoded[0];
            var direction = ent.Comp.PanicWireCut ? ScrubberPumpDirection.Siphoning : src.PumpDirection;

            GasVentScrubberData BuildScrubber() => new()
            {
                Enabled = src.Enabled,
                PumpDirection = direction,
                VolumeRate = MathF.Max(0f, src.VolumeRate),
                WideNet = src.WideNet,
                FilterGases = new HashSet<Gas>(src.FilterGases),
                AirAlarmPanicWireCut = ent.Comp.PanicWireCut,
                Dirty = true
            };

            if (cmd == "set_all_scrubbers")
            {
                foreach (var locAddr in ent.Comp.ScrubberData.Keys.ToList())
                {
                    var clone = BuildScrubber();
                    ent.Comp.ScrubberData[locAddr] = clone;
                    SetData(ent.Owner, locAddr.AddressId, clone);
                }
            }
            else
            {
                var targetAddr = new DeviceAddress(src.AddressId);
                if (_deviceList.ExistsInDeviceList(ent.Owner, targetAddr) ||
                    ent.Comp.ScrubberData.Keys.Any(k => k.AddressId == targetAddr))
                {
                    var scrubData = BuildScrubber();
                    foreach (var locAddr in ent.Comp.ScrubberData.Keys.ToList())
                    {
                        if (locAddr.AddressId == targetAddr)
                            ent.Comp.ScrubberData[locAddr] = scrubData;
                    }
                    SetDeviceData(ent.Owner, targetAddr, scrubData, ent.Comp);
                }
            }

            UpdateUI(ent.Owner, ent.Comp);
        }
        else if (cmd is "set_threshold" or "set_all_thresholds" && packet != null)
        {
            var typeInt = packet.GetInt("ThresholdType", (int) AtmosMonitorThresholdType.Pressure);
            var thresholdType = (AtmosMonitorThresholdType) typeInt;
            var gasInt = packet.GetInt("Gas", -1);
            Gas? gas = gasInt >= 0 ? (Gas) gasInt : null;
            var thresholdDto = AtmosControlCodec.DecodeThreshold(packet.GetString("ThresholdRaw", string.Empty) ?? string.Empty);
            var threshold = thresholdDto.ToThreshold();

            var targets = new List<DeviceAddress>();
            if (cmd == "set_all_thresholds")
            {
                foreach (var locAddr in ent.Comp.SensorData.Keys)
                    targets.Add(locAddr.AddressId);
            }
            else
            {
                var targetAddr = new DeviceAddress(packet.GetInt("AddressId", 0));
                if (targetAddr.IsValid())
                    targets.Add(targetAddr);
            }

            foreach (var addr in targets)
            {
                SetThreshold(ent.Owner, addr, thresholdType, threshold, gas);
                UpdateCachedSensorThreshold(ent.Comp, addr, thresholdType, threshold, gas);
            }

            UpdateUI(ent.Owner, ent.Comp);
        }
        else if (cmd is "apply_sensor_template" or "apply_sensor_template_all" && packet != null)
        {
            var presDto = AtmosControlCodec.DecodeThreshold(packet.GetString("PressureThreshold", string.Empty) ?? string.Empty);
            var tempDto = AtmosControlCodec.DecodeThreshold(packet.GetString("TemperatureThreshold", string.Empty) ?? string.Empty);
            var gasRaw = packet.GetString("GasThresholds", string.Empty) ?? string.Empty;

            var gasThresholds = new Dictionary<Gas, AtmosAlarmThreshold>();
            if (!string.IsNullOrWhiteSpace(gasRaw))
            {
                foreach (var entry in gasRaw.Split('~', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = entry.Split('=', 2);
                    if (kv.Length == 2 && int.TryParse(kv[0], out var gIdx))
                        gasThresholds[(Gas) gIdx] = AtmosControlCodec.DecodeThreshold(kv[1]).ToThreshold();
                }
            }

            var targets = new List<DeviceAddress>();
            if (cmd == "apply_sensor_template_all")
            {
                foreach (var locAddr in ent.Comp.SensorData.Keys)
                    targets.Add(locAddr.AddressId);
            }
            else
            {
                var targetAddr = new DeviceAddress(packet.GetInt("AddressId", 0));
                if (targetAddr.IsValid())
                    targets.Add(targetAddr);
            }

            var presThr = presDto.ToThreshold();
            var tempThr = tempDto.ToThreshold();

            foreach (var addr in targets)
            {
                SetThreshold(ent.Owner, addr, AtmosMonitorThresholdType.Pressure, presThr, null);
                UpdateCachedSensorThreshold(ent.Comp, addr, AtmosMonitorThresholdType.Pressure, presThr, null);

                SetThreshold(ent.Owner, addr, AtmosMonitorThresholdType.Temperature, tempThr, null);
                UpdateCachedSensorThreshold(ent.Comp, addr, AtmosMonitorThresholdType.Temperature, tempThr, null);

                foreach (var (g, gThr) in gasThresholds)
                {
                    SetThreshold(ent.Owner, addr, AtmosMonitorThresholdType.Gas, gThr, g);
                    UpdateCachedSensorThreshold(ent.Comp, addr, AtmosMonitorThresholdType.Gas, gThr, g);
                }
            }

            UpdateUI(ent.Owner, ent.Comp);
        }
    }

    private static void UpdateCachedSensorThreshold(
        AirAlarmComponent alarm,
        DeviceAddress addr,
        AtmosMonitorThresholdType type,
        AtmosAlarmThreshold threshold,
        Gas? gas)
    {
        foreach (var locAddr in alarm.SensorData.Keys.ToList())
        {
            if (locAddr.AddressId != addr)
                continue;

            var cur = alarm.SensorData[locAddr];
            var newPres = type == AtmosMonitorThresholdType.Pressure ? new AtmosAlarmThreshold(threshold) : cur.PressureThreshold;
            var newTemp = type == AtmosMonitorThresholdType.Temperature ? new AtmosAlarmThreshold(threshold) : cur.TemperatureThreshold;
            var newGasMap = cur.GasThresholds != null
                ? new Dictionary<Gas, AtmosAlarmThreshold>(cur.GasThresholds)
                : new Dictionary<Gas, AtmosAlarmThreshold>();
            if (type == AtmosMonitorThresholdType.Gas && gas is { } g)
                newGasMap[g] = new AtmosAlarmThreshold(threshold);

            alarm.SensorData[locAddr] = new AtmosMonitorData(
                cur.Pressure,
                cur.Temperature,
                cur.TotalMoles,
                cur.AlarmState,
                cur.Gases ?? new Dictionary<Gas, float>(),
                newPres,
                newTemp,
                newGasMap);
        }
    }

    private void EmitAirAlarmStatusPacket(EntityUid uid, AirAlarmComponent alarm)
    {
        if (TerminatingOrDeleted(uid) || !TryComp<DeviceLinkSourceComponent>(uid, out var srcComp))
            return;
        var pressure = CalculatePressureAverage(alarm);
        var temperature = CalculateTemperatureAverage(alarm);
        if (!_atmosAlarmable.TryGetHighestAlert(uid, out var highestAlarm))
            highestAlarm = AtmosAlarmType.Normal;

        var o2 = CalculateGasMolarConcentrationAverage(alarm, Gas.Oxygen, out _);
        var n2 = CalculateGasMolarConcentrationAverage(alarm, Gas.Nitrogen, out _);
        var co2 = CalculateGasMolarConcentrationAverage(alarm, Gas.CarbonDioxide, out _);
        var plasma = CalculateGasMolarConcentrationAverage(alarm, Gas.Plasma, out _);

        var ventDtos = new List<AtmosVentEntryDto>(alarm.VentData.Count);
        foreach (var (addr, v) in alarm.VentData)
        {
            ventDtos.Add(new AtmosVentEntryDto(
                addr.AddressId.AddressId,
                addr.ToString(),
                v.Enabled,
                v.PumpDirection,
                v.PressureChecks,
                v.ExternalPressureBound,
                v.InternalPressureBound));
        }
        ventDtos.Sort((a, b) => string.Compare(a.DisplayAddress, b.DisplayAddress, StringComparison.OrdinalIgnoreCase));

        var scrubberDtos = new List<AtmosScrubberEntryDto>(alarm.ScrubberData.Count);
        foreach (var (addr, s) in alarm.ScrubberData)
        {
            scrubberDtos.Add(new AtmosScrubberEntryDto(
                addr.AddressId.AddressId,
                addr.ToString(),
                s.Enabled,
                s.PumpDirection,
                s.VolumeRate,
                s.WideNet,
                alarm.PanicWireCut || s.AirAlarmPanicWireCut,
                s.FilterGases.OrderBy(g => (int) g).ToList()));
        }
        scrubberDtos.Sort((a, b) => string.Compare(a.DisplayAddress, b.DisplayAddress, StringComparison.OrdinalIgnoreCase));

        var sensorDtos = new List<AtmosSensorEntryDto>(alarm.SensorData.Count);
        foreach (var (addr, sens) in alarm.SensorData)
        {
            var gasThrDtos = new Dictionary<Gas, AtmosThresholdDto>();
            if (sens.GasThresholds != null)
            {
                foreach (var (g, thr) in sens.GasThresholds)
                    gasThrDtos[g] = AtmosThresholdDto.FromThreshold(thr);
            }

            sensorDtos.Add(new AtmosSensorEntryDto(
                addr.AddressId.AddressId,
                addr.ToString(),
                sens.AlarmState,
                sens.Pressure,
                sens.Temperature,
                sens.TotalMoles,
                sens.Gases != null ? new Dictionary<Gas, float>(sens.Gases) : new Dictionary<Gas, float>(),
                AtmosThresholdDto.FromThreshold(sens.PressureThreshold),
                AtmosThresholdDto.FromThreshold(sens.TemperatureThreshold),
                gasThrDtos));
        }
        sensorDtos.Sort((a, b) => string.Compare(a.DisplayAddress, b.DisplayAddress, StringComparison.OrdinalIgnoreCase));

        var pkt = new ComputerPacket("AirAlarmStatus");
        pkt.Set("AlarmLevel", highestAlarm.Value.ToString());
        pkt.Set("Mode", alarm.CurrentMode.ToString());
        pkt.Set("AutoMode", alarm.AutoMode.ToString());
        pkt.Set("Locked", _lock.IsLocked(uid).ToString());
        pkt.Set("Pressure", pressure.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        pkt.Set("TemperatureC", (temperature - 273.15f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        pkt.Set("Vents", alarm.VentData.Count);
        pkt.Set("Scrubbers", alarm.ScrubberData.Count);
        pkt.Set("Sensors", alarm.SensorData.Count);
        pkt.Set("GasSummary", $"O2: {o2:0.0} mol | N2: {n2:0.0} mol | CO2: {co2:0.0} mol | Plasma: {plasma:0.0} mol");
        pkt.Set("VentsRaw", AtmosControlCodec.EncodeVents(ventDtos));
        pkt.Set("ScrubbersRaw", AtmosControlCodec.EncodeScrubbers(scrubberDtos));
        pkt.Set("SensorsRaw", AtmosControlCodec.EncodeSensors(sensorDtos));

        _deviceLink.InvokePort((uid, srcComp), PortAirAlarmStatusSource, ref pkt);
        _deviceLink.InvokePort((uid, srcComp), PortMachineStatusSender, ref pkt);
    }

    private void OnDeviceListUpdate(EntityUid uid, AirAlarmComponent component, ref DeviceListUpdateEvent args)
    {
        foreach (var device in args.OldDevices)
        {
            if (!_deviceNetworkQuery.TryGetComponent(device, out var deviceNet))
            {
                continue;
            }

            _atmosDevNet.Deregister(uid, deviceNet.Address);
        }

        component.ScrubberData.Clear();
        component.SensorData.Clear();
        component.VentData.Clear();
        component.KnownDevices.Clear();

        UpdateUI(uid, component);

        SyncRegisterAllDevices(uid);
    }

    private void OnPowerChanged(EntityUid uid, AirAlarmComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            return;
        }

        ForceCloseAllInterfaces(uid);
        component.CurrentModeUpdater = null;
        component.KnownDevices.Clear();
        component.ScrubberData.Clear();
        component.SensorData.Clear();
        component.VentData.Clear();
    }

    private void OnClose(EntityUid uid, AirAlarmComponent component, BoundUIClosedEvent args)
    {
        if (!_ui.IsUiOpen(uid, SharedAirAlarmInterfaceKey.Key))
            RemoveActiveInterface(uid);
    }

    private void OnInit(EntityUid uid, AirAlarmComponent comp, ComponentInit args)
    {
        _deviceLink.EnsureSourcePorts(uid, comp.DangerPort, comp.WarningPort, comp.NormalPort);
    }

    private void OnMapInit(EntityUid uid, AirAlarmComponent comp, MapInitEvent args)
    {
        // for mapped linked air alarms, start with high so when it changes for the first time it goes from high to low
        // without this the output would suddenly get sent a low signal after nothing which is bad
        _deviceLink.SendSignal(uid, GetPort(comp), true);
    }

    private void OnShutdown(EntityUid uid, AirAlarmComponent component, ComponentShutdown args)
    {
        _activeUserInterfaces.Remove(uid);
    }

    private void OnActivate(EntityUid uid, AirAlarmComponent component, AfterActivatableUIOpenEvent args)
    {
        AddActiveInterface(uid);
        SyncAllDevices(uid);
        UpdateUI(uid, component);
    }

    private void OnResyncAll(EntityUid uid, AirAlarmComponent component, AirAlarmResyncAllDevicesMessage args)
    {
        if (!AccessCheck(uid, args.Actor, component))
        {
            return;
        }

        component.KnownDevices.Clear();
        component.VentData.Clear();
        component.ScrubberData.Clear();
        component.SensorData.Clear();

        SyncRegisterAllDevices(uid);
    }

    private void OnUpdateAlarmMode(EntityUid uid, AirAlarmComponent component, AirAlarmUpdateAlarmModeMessage args)
    {
        if (AccessCheck(uid, args.Actor, component))
        {
            var addr = DeviceAddress.Invalid;
            if (TryComp<DeviceNetworkComponent>(uid, out var netConn))
            {
                addr = netConn.Address;
            }

            _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} changed {ToPrettyString(uid)} mode to {args.Mode}");
            SetMode(uid, addr, args.Mode, false);
        }
        else
        {
            UpdateUI(uid, component);
        }
    }

    private void OnUpdateAutoMode(EntityUid uid, AirAlarmComponent component, AirAlarmUpdateAutoModeMessage args)
    {
        if (!AccessCheck(uid, args.Actor, component))
        {
            UpdateUI(uid, component);
            return;
        }

        component.AutoMode = args.Enabled;

        _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} changed {ToPrettyString(uid)} auto mode to {args.Enabled}");
        UpdateUI(uid, component);
    }

    private void OnUpdateThreshold(EntityUid uid, AirAlarmComponent component, AirAlarmUpdateAlarmThresholdMessage args)
    {
        if (AccessCheck(uid, args.Actor, component))
        {
            if (args.Gas != null)
                _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} changed {args.Address} {args.Gas} {args.Type} threshold using {ToPrettyString(uid)}");
            else
                _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} changed {args.Address} {args.Type} threshold using {ToPrettyString(uid)}");

            SetThreshold(uid, args.Address, args.Type, args.Threshold, args.Gas);
        }
        else
        {
            UpdateUI(uid, component);
        }
    }

    private void OnUpdateDeviceData(EntityUid uid, AirAlarmComponent component, AirAlarmUpdateDeviceDataMessage args)
    {
        if (AccessCheck(uid, args.Actor, component)
            && _deviceList.ExistsInDeviceList(uid, args.Address))
        {
            _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} changed {args.Address} settings using {ToPrettyString(uid)}");

            SetDeviceData(uid, args.Address, args.Data);
        }
        else
        {
            UpdateUI(uid, component);
        }
    }

    private void OnCopyDeviceData(EntityUid uid, AirAlarmComponent component, AirAlarmCopyDeviceDataMessage args)
    {
        if (!AccessCheck(uid, args.Actor, component))
        {
           UpdateUI(uid, component);
            return;
        }

        switch (args.Data)
        {
            case GasVentPumpData ventData:
                foreach (var addr in component.VentData.Keys)
                {
                    _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} copied settings to vent {addr}");
                    SetData(uid, addr.AddressId, ventData);
                }
                break;

            case GasVentScrubberData scrubberData:
                foreach (var addr in component.ScrubberData.Keys)
                {
                    _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Medium, $"{ToPrettyString(args.Actor)} copied settings to scrubber {addr}");
                    SetData(uid, addr.AddressId, scrubberData);
                }
                break;

            case AtmosMonitorData sensorData:
                foreach (var addr in component.SensorData.Keys)
                {
                    SetAllThresholds(uid, addr.AddressId, sensorData);
                }
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnLockToggled(Entity<AirAlarmComponent> ent, ref LockToggledEvent args)
    {
        EmitAirAlarmStatusPacket(ent.Owner, ent.Comp);
    }

    private bool AccessCheck(EntityUid uid, EntityUid? user, AirAlarmComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (HasComp<LockComponent>(uid))
        {
            if (!_lock.IsLocked(uid))
                return true;

            if (user != null)
            {
                _popup.PopupEntity(Loc.GetString("air-alarm-ui-access-denied"), user.Value, user.Value);
                _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Low, $"{ToPrettyString(user)} attempted to access locked {ToPrettyString(uid)}");
            }

            return false;
        }

        // if it has no access reader behave as if the user has AA
        if (!TryComp<AccessReaderComponent>(uid, out var reader))
            return true;

        if (user == null)
            return false;

        if (!_access.IsAllowed(user.Value, uid, reader))
        {
            _popup.PopupEntity(Loc.GetString("air-alarm-ui-access-denied"), user.Value, user.Value);
            _adminLogger.Add(LogType.AtmosDeviceSetting, LogImpact.Low, $"{ToPrettyString(user)} attempted to access {ToPrettyString(uid)} without access");
            return false;
        }

        return true;
    }

    private void OnAtmosAlarm(EntityUid uid, AirAlarmComponent component, AtmosAlarmEvent args)
    {
        var embeddedUiOpen = TryComp<EmbeddedComputerHostComponent>(uid, out var embHost) &&
                             embHost.ComputerEntity is { } embComp &&
                             _ui.IsUiOpen(embComp, ComputerUiKey.Key);
        if (_ui.IsUiOpen(uid, SharedAirAlarmInterfaceKey.Key) || embeddedUiOpen)
        {
            SyncAllDevices(uid);
        }

        var addr = DeviceAddress.Invalid;
        if (TryComp<DeviceNetworkComponent>(uid, out var netConn))
        {
            addr = netConn.Address;
        }

        if (component.AutoMode)
        {
            if (args.AlarmType == AtmosAlarmType.Danger)
            {
                SetMode(uid, addr, AirAlarmMode.WideFiltering, false);
            }
            else if (args.AlarmType == AtmosAlarmType.Normal || args.AlarmType == AtmosAlarmType.Warning)
            {
                SetMode(uid, addr, AirAlarmMode.Filtering, false);
            }
        }

        if (component.State != args.AlarmType)
        {
            // send low to old state's port
            _deviceLink.SendSignal(uid, GetPort(component), false);

            // send high to new state's port, along with updating the cached state
            component.State = args.AlarmType;
            _deviceLink.SendSignal(uid, GetPort(component), true);
        }

        UpdateUI(uid, component);
    }

    private string GetPort(AirAlarmComponent comp)
    {
        if (comp.State == AtmosAlarmType.Danger)
            return comp.DangerPort;

        if (comp.State == AtmosAlarmType.Warning)
            return comp.WarningPort;

        return comp.NormalPort;
    }

    #endregion

    #region Air Alarm Settings

    /// <summary>
    ///     Set an air alarm's mode.
    /// </summary>
    /// <param name="origin">The origin address of this mode set. Used for network sync.</param>
    /// <param name="mode">The mode to set the alarm to.</param>
    /// <param name="uiOnly">Whether this change is for the UI only, or if it changes the air alarm's operating mode. Defaults to true.</param>
    public void SetMode(EntityUid uid, DeviceAddress origin, AirAlarmMode mode, bool uiOnly = true, AirAlarmComponent? controller = null)
    {
        if (!Resolve(uid, ref controller))
        {
            return;
        }

        if (controller.PanicWireCut)
        {
            mode = AirAlarmMode.Panic;
        }


        controller.CurrentMode = mode;

        // setting it to UI only means we don't have
        // to deal with the issue of not-single-owner
        // alarm mode executors
        if (!uiOnly)
        {
            var newMode = AirAlarmModeFactory.ModeToExecutor(mode);
            if (newMode != null)
            {
                newMode.Execute(uid);
                if (newMode is IAirAlarmModeUpdate updatedMode)
                {
                    controller.CurrentModeUpdater = updatedMode;
                    controller.CurrentModeUpdater.NetOwner = origin;
                }
                else if (controller.CurrentModeUpdater != null)
                    controller.CurrentModeUpdater = null;
            }
        }
        // only one air alarm in a network can use an air alarm mode
        // that updates, so even if it's a ui-only change,
        // we have to invalidate the last mode's updater and
        // remove it because otherwise it'll execute a now
        // invalid mode
        else if (controller.CurrentModeUpdater != null
                 && controller.CurrentModeUpdater.NetOwner != origin)
        {
            controller.CurrentModeUpdater = null;
        }

        UpdateUI(uid, controller);

        // setting sync deals with the issue of air alarms
        // in the same network needing to have the same mode
        // as other alarms
        SyncMode(uid, mode);
    }

    /// <summary>
    ///     Sets device data.
    /// </summary>
    /// <param name="address">The address to send the new data to.</param>
    /// <param name="devData">The device data to be sent.</param>
    private void SetDeviceData(EntityUid uid, DeviceAddress address, IAtmosDeviceData devData, AirAlarmComponent? controller = null)
    {
        if (!Resolve(uid, ref controller))
        {
            return;
        }

        devData.Dirty = true;

        devData.RaisePayload(uid, address, _deviceNet);
        SetDeviceDataInternal(uid, address, devData);
    }

    [SubscribeLocalEvent]
    private void OnSetData(Entity<AirAlarmComponent> ent, ref DeviceNetworkPacketEvent<AirAlarmSetDataPayload> args)
    {
        var (uid, controller) = ent;
        if (!controller.CanSync)
            return;

        var senderAddress = args.Sender.Comp.LocAddress;

        // Save into component.
        // Sync data to interface.
        switch (args.Data.Payload)
        {
            case GasVentPumpData ventData:
                if (!controller.VentData.TryAdd(senderAddress, ventData))
                    controller.VentData[senderAddress] = ventData;
                break;
            case GasVentScrubberData scrubberData:
                if (!controller.ScrubberData.TryAdd(senderAddress, scrubberData))
                    controller.ScrubberData[senderAddress] = scrubberData;
                break;
            case AtmosMonitorData sensorData:
                if (!controller.SensorData.TryAdd(senderAddress, sensorData))
                    controller.SensorData[senderAddress] = sensorData;
                break;
        }

        controller.KnownDevices.Add(senderAddress);
        UpdateUI(uid, controller);
    }

    [SubscribeLocalEvent]
    private void OnSetMode(Entity<AirAlarmComponent> ent, ref DeviceNetworkPacketEvent<AirAlarmSetModePayload> args)
    {
        SetMode(ent, args.SenderAddress, args.Data.Mode, uiOnly: false);
    }

    #endregion

    #region UI

    // List of active user interfaces.
    private readonly HashSet<EntityUid> _activeUserInterfaces = new();

    /// <summary>
    ///     Adds an active interface to be updated.
    /// </summary>
    private void AddActiveInterface(EntityUid uid)
    {
        _activeUserInterfaces.Add(uid);
    }

    /// <summary>
    ///     Removes an active interface from the system update loop.
    /// </summary>
    private void RemoveActiveInterface(EntityUid uid)
    {
        _activeUserInterfaces.Remove(uid);
    }

    /// <summary>
    ///     Force closes all interfaces currently open related to this air alarm.
    /// </summary>
    private void ForceCloseAllInterfaces(EntityUid uid)
    {
        _ui.CloseUi(uid, SharedAirAlarmInterfaceKey.Key);
    }

    private void OnAtmosUpdate(EntityUid uid, AirAlarmComponent alarm, ref AtmosDeviceUpdateEvent args)
    {
        alarm.CurrentModeUpdater?.Update(uid);
    }

    public float CalculatePressureAverage(AirAlarmComponent alarm)
    {
        return alarm.SensorData.Count != 0
            ? alarm.SensorData.Values.Select(v => v.Pressure).Average()
            : 0f;
    }

    public float CalculateTemperatureAverage(AirAlarmComponent alarm)
    {
        return alarm.SensorData.Count != 0
            ? alarm.SensorData.Values.Select(v => v.Temperature).Average()
            : 0f;
    }
    public float CalculateGasMolarConcentrationAverage(AirAlarmComponent alarm, Gas gas, out float percentage)
    {
        percentage = 0f;

        var data = alarm.SensorData.Values
            .Where(v => v.Gases != null)
            .SelectMany(v => v.Gases.Where(g => g.Key == gas))
            .ToList();

        if (data.Count == 0)
            return 0f;

        var averageMol = data.Select(kvp => kvp.Value).Average();
        var totalMoles = alarm.SensorData.Values.Select(v => v.TotalMoles).Sum();
        percentage = totalMoles > 0f ? data.Select(kvp => kvp.Value).Sum() / totalMoles : 0f;

        return averageMol;
    }

    public void UpdateUI(EntityUid uid, AirAlarmComponent? alarm = null, DeviceNetworkComponent? devNet = null, AtmosAlarmableComponent? alarmable = null)
    {
        if (!Resolve(uid, ref alarm, ref devNet, ref alarmable))
        {
            return;
        }

        var pressure = CalculatePressureAverage(alarm);
        var temperature = CalculateTemperatureAverage(alarm);
        var dataToSend = new List<(LocDeviceAddress, IAtmosDeviceData)>();

        foreach (var (addr, data) in alarm.VentData)
        {
            dataToSend.Add((addr, data));
        }
        foreach (var (addr, data) in alarm.ScrubberData)
        {
            data.AirAlarmPanicWireCut = alarm.PanicWireCut;
            dataToSend.Add((addr, data));
        }
        foreach (var (addr, data) in alarm.SensorData)
        {
            dataToSend.Add((addr, data));
        }

        var deviceCount = alarm.KnownDevices.Count;

        if (!_atmosAlarmable.TryGetHighestAlert(uid, out var highestAlarm))
        {
            highestAlarm = AtmosAlarmType.Normal;
        }

        _ui.SetUiState(
            uid,
            SharedAirAlarmInterfaceKey.Key,
            new AirAlarmUIState((devNet.Address, devNet.Prefix), deviceCount, pressure, temperature, dataToSend, alarm.CurrentMode, highestAlarm.Value, alarm.AutoMode, alarm.PanicWireCut));

        EmitAirAlarmStatusPacket(uid, alarm);
    }

    private const float Delay = 8f;
    private float _timer;

    public override void Update(float frameTime)
    {
        _timer += frameTime;
        if (_timer >= Delay)
        {
            _timer = 0f;
            var synced = new HashSet<EntityUid>();
            foreach (var uid in _activeUserInterfaces)
            {
                if (synced.Add(uid))
                    SyncAllSensors(uid);
            }

            var embQuery = EntityQueryEnumerator<AirAlarmComponent, EmbeddedComputerHostComponent>();
            while (embQuery.MoveNext(out var uid, out var alarm, out var host))
            {
                if (host.ComputerEntity is not { } compUid || !_ui.IsUiOpen(compUid, ComputerUiKey.Key))
                    continue;

                if (synced.Add(uid))
                    SyncAllSensors(uid);
                EmitAirAlarmStatusPacket(uid, alarm);
            }
        }
    }

    #endregion
}
