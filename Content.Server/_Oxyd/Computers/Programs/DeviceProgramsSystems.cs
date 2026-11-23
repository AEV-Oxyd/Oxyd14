using System.Globalization;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Lock;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Research.Prototypes;
using Content.Shared.StationRecords;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Pure DeviceLink + Hard-Drive file systems for APC Control, Air Alarm Control, and Autolathe Control.
/// Supports both local embedded host control and remote station APC / Atmos control with per-APC
/// authorization via .acc files or ID card swipes.
/// </summary>
public sealed partial class DeviceProgramsSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<OxydComputerComponent> _computerQuery;
    private EntityQuery<ApcComponent> _apcQuery;
    private EntityQuery<AirAlarmComponent> _airAlarmQuery;
    private EntityQuery<IdCardComponent> _idCardQuery;

    private static readonly SoundPathSpecifier AuthSwipeSound = new("/Audio/Machines/id_swipe.ogg");
    private static readonly ProtoId<AccessLevelPrototype> EngineeringAccess = "Engineering";
    private static readonly ProtoId<AccessLevelPrototype> AtmosphericsAccess = "Atmospherics";
    private static readonly ProtoId<AccessLevelPrototype> CommandAccess = "Command";

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();
        _computerQuery = GetEntityQuery<OxydComputerComponent>();
        _apcQuery = GetEntityQuery<ApcComponent>();
        _airAlarmQuery = GetEntityQuery<AirAlarmComponent>();
        _idCardQuery = GetEntityQuery<IdCardComponent>();

        // APC Control
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramStartupEvent>(OnApcStartup);
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramActivatedEvent>(OnApcActivated);
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnApcDeviceLink);
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramRelayedEvent<InteractUsingEvent>>(OnApcInteractUsing);
        Subs.BuiEvents<ApcControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnApcBuiOpened);
            subs.Event<ApcControlActionMessage>(OnApcUiAction);
        });

        // Atmos / AirAlarm Control
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramStartupEvent>(OnAtmosStartup);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramActivatedEvent>(OnAtmosActivated);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnAtmosDeviceLink);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramRelayedEvent<InteractUsingEvent>>(OnAtmosInteractUsing);
        Subs.BuiEvents<AtmosControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnAtmosBuiOpened);
            subs.Event<AtmosControlActionMessage>(OnAtmosUiAction);
            subs.Event<AtmosUpdateVentMessage>(OnAtmosUpdateVent);
            subs.Event<AtmosUpdateScrubberMessage>(OnAtmosUpdateScrubber);
            subs.Event<AtmosUpdateSensorThresholdMessage>(OnAtmosUpdateSensorThreshold);
            subs.Event<AtmosSaveTemplateMessage>(OnAtmosSaveTemplate);
            subs.Event<AtmosDeleteTemplateMessage>(OnAtmosDeleteTemplate);
            subs.Event<AtmosApplyTemplateMessage>(OnAtmosApplyTemplate);
        });

        // Lathe Control
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramStartupEvent>(OnLatheStartup);
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramActivatedEvent>(OnLatheActivated);
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnLatheDeviceLink);
        SubscribeLocalEvent<FileSystemComponent, FileSystemChangedEvent>(OnDriveFileSystemChanged);
        Subs.BuiEvents<LatheControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnLatheBuiOpened);
            subs.Event<LatheControlActionMessage>(OnLatheUiAction);
        });
    }

    // =========================================================================
    // APC CONTROL
    // =========================================================================

    private void OnApcStartup(Entity<ApcControlProgramComponent> ent, ref ProgramStartupEvent args)
    {
        RequestApcStatus(args.ComputerUid, ent.Comp);
        UpdateApcUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnApcActivated(Entity<ApcControlProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        RequestApcStatus(args.ComputerUid, ent.Comp);
        UpdateApcUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnApcBuiOpened(Entity<ApcControlProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        RequestApcStatus(compUid, ent.Comp, args.Actor);
        UpdateApcUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnApcDeviceLink(Entity<ApcControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "ApcStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.AreaName = pkt.GetString("AreaName", ent.Comp.AreaName) ?? ent.Comp.AreaName;
        ent.Comp.MainBreakerEnabled = pkt.GetString("MainBreaker", "False") == "True";
        ent.Comp.BreakerTripped = pkt.GetString("BreakerTripped", "False") == "True";
        ent.Comp.Locked = pkt.GetString("Locked", "True") == "True";
        if (float.TryParse(pkt.GetString("ChargePercent", "0"), out var pct))
            ent.Comp.ChargePercent = pct;
        ent.Comp.LoadWatts = pkt.GetInt("LoadWatts", 0);
        ent.Comp.ExternalPowerState = pkt.GetString("ExternalPower", "None") ?? "None";
        ent.Comp.StatusMessage = string.Empty;
        Dirty(ent);

        UpdateApcUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnApcInteractUsing(Entity<ApcControlProgramComponent> ent, ref ProgramRelayedEvent<InteractUsingEvent> relayed)
    {
        if (relayed.Args.Handled || !TryExtractIdCard(relayed.Args.Used, out var cardUid))
            return;

        var compUid = relayed.ComputerUid;
        GetAvailableApcTargets(compUid, ent.Comp, out var selectedApcUid, out var selectedGuid);
        if (selectedApcUid == null || string.IsNullOrEmpty(selectedGuid))
            return;

        if (CanIdCardAuthorizeDevice(cardUid, selectedApcUid.Value, isAtmos: false))
        {
            GrantEngineeringApcAuthorization(compUid, selectedGuid, selectedApcUid.Value, relayed.Args.User);
            var cardName = MetaData(cardUid).EntityName;
            ent.Comp.StatusMessage = $"Authorized to {MetaData(selectedApcUid.Value).EntityName} [{selectedGuid}] via swiped ID ({cardName}).";
            _audio.PlayPvs(AuthSwipeSound, relayed.Args.Target);
            _popup.PopupEntity($"Authorized to {MetaData(selectedApcUid.Value).EntityName}.", relayed.Args.Target, relayed.Args.User);
        }
        else
        {
            ent.Comp.StatusMessage = $"Authorization denied for {MetaData(selectedApcUid.Value).EntityName}: ID card lacks Engineering/APC access.";
            _popup.PopupEntity("Access denied for selected APC.", relayed.Args.Target, relayed.Args.User);
        }

        relayed.Args.Handled = true;
        Dirty(ent);
        RequestApcStatus(compUid, ent.Comp, relayed.Args.User);
        UpdateApcUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnApcUiAction(Entity<ApcControlProgramComponent> ent, ref ApcControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case ApcControlAction.SelectTarget:
                if (!string.IsNullOrWhiteSpace(args.Argument))
                {
                    ent.Comp.SelectedTargetGuid = args.Argument.Trim();
                    ent.Comp.StatusMessage = string.Empty;
                    Dirty(ent);
                }
                RequestApcStatus(compUid, ent.Comp, args.Actor);
                UpdateApcUi(ent.Owner, compUid, ent.Comp);
                return;

            case ApcControlAction.SelectAccFile:
                ent.Comp.SelectedAccFilePath = string.IsNullOrWhiteSpace(args.Argument) ? null : args.Argument.Trim();
                Dirty(ent);
                UpdateApcUi(ent.Owner, compUid, ent.Comp);
                return;

            case ApcControlAction.AuthorizeWithAcc:
                GetAvailableApcTargets(compUid, ent.Comp, out var authApcUid, out var authApcGuid);
                if (authApcUid == null || string.IsNullOrEmpty(authApcGuid))
                {
                    ent.Comp.StatusMessage = "No target APC found.";
                    UpdateApcUi(ent.Owner, compUid, ent.Comp);
                    return;
                }

                var reqPath = !string.IsNullOrWhiteSpace(args.Argument) ? args.Argument.Trim() : ent.Comp.SelectedAccFilePath;
                if (!TryFindAccFile(compUid, reqPath, out var accFile))
                {
                    ent.Comp.StatusMessage = "Authorization failed: No .acc file found in /keys/.";
                    UpdateApcUi(ent.Owner, compUid, ent.Comp);
                    return;
                }

                ent.Comp.SelectedAccFilePath = accFile.FullPath;
                if (CanAccFileAuthorizeDevice(accFile, authApcUid.Value, isAtmos: false))
                {
                    GrantEngineeringApcAuthorization(compUid, authApcGuid, authApcUid.Value, args.Actor);
                    ent.Comp.StatusMessage = $"Authorized to {MetaData(authApcUid.Value).EntityName} [{authApcGuid}] via {accFile.FullPath}.";
                }
                else
                {
                    ent.Comp.StatusMessage = $"Access denied: {accFile.FullPath} lacks required Engineering/APC access for {MetaData(authApcUid.Value).EntityName}.";
                }

                Dirty(ent);
                RequestApcStatus(compUid, ent.Comp, args.Actor);
                UpdateApcUi(ent.Owner, compUid, ent.Comp);
                return;

            case ApcControlAction.RevokeAuth:
                GetAvailableApcTargets(compUid, ent.Comp, out _, out var revGuid);
                if (!string.IsNullOrEmpty(revGuid))
                {
                    ent.Comp.AuthorizedTargetGuids.Remove(revGuid);
                    ent.Comp.StatusMessage = $"Revoked remote session authorization for [{revGuid}].";
                    Dirty(ent);
                }
                UpdateApcUi(ent.Owner, compUid, ent.Comp);
                return;
        }

        GetAvailableApcTargets(compUid, ent.Comp, out var targetApcUid, out var targetGuid);
        var isAuthorized = targetApcUid != null && IsApcTargetAuthorized(compUid, ent.Comp, targetApcUid.Value, targetGuid);

        if (!isAuthorized || ent.Comp.Locked)
        {
            ent.Comp.StatusMessage = "Action blocked: APC is LOCKED / UNAUTHORIZED. Swipe authorized ID card or authorize with a .acc file.";
            RequestApcStatus(compUid, ent.Comp, args.Actor);
            UpdateApcUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        switch (args.Action)
        {
            case ApcControlAction.Refresh:
                RequestApcStatus(compUid, ent.Comp, args.Actor);
                break;

            case ApcControlAction.ToggleBreaker:
                SendApcDeviceLink(compUid, ent.Comp, "toggle_breaker", string.Empty, args.Actor);
                break;

            case ApcControlAction.SetBreakerOn:
                SendApcDeviceLink(compUid, ent.Comp, "set_breaker", "on", args.Actor);
                break;

            case ApcControlAction.SetBreakerOff:
                SendApcDeviceLink(compUid, ent.Comp, "set_breaker", "off", args.Actor);
                break;

            case ApcControlAction.SaveLog:
                if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
                {
                    var textFile = new TextVirtualFile
                    {
                        Name = "apc_snapshot",
                        Extension = "txt",
                        Directory = "/logs/",
                        TextContent = $"Area: {ent.Comp.AreaName}\nBreaker: {(ent.Comp.MainBreakerEnabled ? "ENABLED" : "OFF")}\nCharge: {ent.Comp.ChargePercent:0.0}%\nLoad: {ent.Comp.LoadWatts} W\nExternal: {ent.Comp.ExternalPowerState}",
                        Size = 256
                    };
                    _fileSystem.TryWriteFile(driveUid.Value, textFile, fs);
                    ent.Comp.StatusMessage = "Saved APC telemetry snapshot to /logs/apc_snapshot.txt.";
                }
                else
                {
                    ent.Comp.StatusMessage = "Save failed: No hard drive installed.";
                }
                break;
        }

        UpdateApcUi(ent.Owner, compUid, ent.Comp);
    }

    private void RequestApcStatus(EntityUid compUid, ApcControlProgramComponent comp, EntityUid? actor = null)
    {
        SendApcDeviceLink(compUid, comp, "status", string.Empty, actor);
    }

    private void SendApcDeviceLink(EntityUid compUid, ApcControlProgramComponent comp, string cmd, string arg, EntityUid? actor)
    {
        var pkt = new ComputerPacket(cmd);
        pkt.Set("Argument", arg);

        GetAvailableApcTargets(compUid, comp, out var targetApcUid, out var targetGuid);
        var isLocalHostApc = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine == targetApcUid;

        if (!isLocalHostApc && targetApcUid is { Valid: true } remoteApcUid && Exists(remoteApcUid))
        {
            var isAuth = IsApcTargetAuthorized(compUid, comp, remoteApcUid, targetGuid);
            if (isAuth && cmd is not ("status" or "status_request") && _lock.IsLocked(remoteApcUid))
                _lock.Unlock(remoteApcUid, actor);

            DispatchPacketToRemoteHost(remoteApcUid, "ApcCommandSink", pkt, actor);
            SyncApcTelemetryFromTarget(compUid, comp, remoteApcUid, targetGuid);
            return;
        }

        var sent = _computer.TryInvokeDeviceLinkPort(compUid, "ApcCommandSink", isHostPort: true, cmd, arg, out var err, actor, pkt);
        if (!sent)
        {
            // Fallback to MachineCommandReceiver if host only defines generic sink
            sent = _computer.TryInvokeDeviceLinkPort(compUid, "MachineCommandReceiver", isHostPort: true, cmd, arg, out err, actor, pkt);
        }

        if (!sent && targetApcUid == null)
            comp.StatusMessage = $"DeviceLink error: {err}";
    }

    private void UpdateApcUi(EntityUid progUid, EntityUid compUid, ApcControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
        var isRemoteConsole = cComp?.HostMachine == null || !_apcQuery.HasComp(cComp.HostMachine.Value);
        var targets = GetAvailableApcTargets(compUid, comp, out var selectedApcUid, out var selectedGuid);
        var accFiles = GetAvailableAccFilePaths(compUid, ref comp.SelectedAccFilePath);
        var isAuthorized = selectedApcUid != null && IsApcTargetAuthorized(compUid, comp, selectedApcUid.Value, selectedGuid);

        if (selectedApcUid != null && (isRemoteConsole || !comp.HasTelemetry))
        {
            SyncApcTelemetryFromTarget(compUid, comp, selectedApcUid.Value, selectedGuid);
            isAuthorized = IsApcTargetAuthorized(compUid, comp, selectedApcUid.Value, selectedGuid);
        }
        else if (selectedApcUid != null)
        {
            comp.Locked = !isAuthorized;
        }

        var state = new ApcControlUiState(
            comp.HasTelemetry,
            dlEnabled,
            comp.AreaName,
            comp.MainBreakerEnabled,
            comp.BreakerTripped,
            comp.Locked,
            comp.ChargePercent,
            comp.LoadWatts,
            comp.ExternalPowerState,
            comp.StatusMessage,
            selectedGuid,
            isAuthorized,
            comp.SelectedAccFilePath,
            accFiles,
            targets,
            isRemoteConsole);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    // =========================================================================
    // ATMOS / AIR ALARM CONTROL
    // =========================================================================

    private void OnAtmosStartup(Entity<AtmosControlProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SendAtmosDeviceLink(args.ComputerUid, ent.Comp, "status", string.Empty, null);
        UpdateAtmosUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnAtmosActivated(Entity<AtmosControlProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SendAtmosDeviceLink(args.ComputerUid, ent.Comp, "status", string.Empty, null);
        UpdateAtmosUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnAtmosBuiOpened(Entity<AtmosControlProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosDeviceLink(Entity<AtmosControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "AirAlarmStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.AlarmLevel = pkt.GetString("AlarmLevel", ent.Comp.AlarmLevel) ?? ent.Comp.AlarmLevel;
        ent.Comp.CurrentMode = pkt.GetString("Mode", ent.Comp.CurrentMode) ?? ent.Comp.CurrentMode;
        ent.Comp.AutoMode = pkt.GetString("AutoMode", "True") == "True";
        ent.Comp.Locked = pkt.GetString("Locked", "True") == "True";
        if (float.TryParse(pkt.GetString("Pressure", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var pres))
            ent.Comp.PressureKpa = pres;
        if (float.TryParse(pkt.GetString("TemperatureC", "20"), NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
            ent.Comp.TemperatureCelsius = temp;
        ent.Comp.VentCount = pkt.GetInt("Vents", 0);
        ent.Comp.ScrubberCount = pkt.GetInt("Scrubbers", 0);
        ent.Comp.SensorCount = pkt.GetInt("Sensors", 0);
        ent.Comp.GasSummary = pkt.GetString("GasSummary", ent.Comp.GasSummary) ?? ent.Comp.GasSummary;
        ent.Comp.Vents = AtmosControlCodec.DecodeVents(pkt.GetString("VentsRaw", string.Empty) ?? string.Empty);
        ent.Comp.Scrubbers = AtmosControlCodec.DecodeScrubbers(pkt.GetString("ScrubbersRaw", string.Empty) ?? string.Empty);
        ent.Comp.Sensors = AtmosControlCodec.DecodeSensors(pkt.GetString("SensorsRaw", string.Empty) ?? string.Empty);
        ent.Comp.StatusMessage = string.Empty;
        Dirty(ent);

        UpdateAtmosUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnAtmosInteractUsing(Entity<AtmosControlProgramComponent> ent, ref ProgramRelayedEvent<InteractUsingEvent> relayed)
    {
        if (relayed.Args.Handled || !TryExtractIdCard(relayed.Args.Used, out var cardUid))
            return;

        var compUid = relayed.ComputerUid;
        GetAvailableAtmosTargets(compUid, ent.Comp, out var selectedAlarmUid, out var selectedGuid, out var selectedApcUid, out var selectedApcGuid);
        if (selectedAlarmUid == null || string.IsNullOrEmpty(selectedGuid))
            return;

        var checkUid = selectedApcUid ?? selectedAlarmUid.Value;
        if (CanIdCardAuthorizeDevice(cardUid, checkUid, isAtmos: true) ||
            CanIdCardAuthorizeDevice(cardUid, selectedAlarmUid.Value, isAtmos: true))
        {
            GrantEngineeringApcAuthorization(compUid, selectedApcGuid, checkUid, relayed.Args.User, selectedGuid, selectedAlarmUid.Value);
            var cardName = MetaData(cardUid).EntityName;
            ent.Comp.StatusMessage = $"Authorized to {MetaData(selectedAlarmUid.Value).EntityName} [APC {selectedApcGuid}] via swiped ID ({cardName}).";
            _audio.PlayPvs(AuthSwipeSound, relayed.Args.Target);
            _popup.PopupEntity($"Authorized to {MetaData(selectedAlarmUid.Value).EntityName}.", relayed.Args.Target, relayed.Args.User);
        }
        else
        {
            ent.Comp.StatusMessage = $"Authorization denied for {MetaData(selectedAlarmUid.Value).EntityName}: ID card lacks Atmos/Engineering access.";
            _popup.PopupEntity("Access denied for selected APC / Air Alarm.", relayed.Args.Target, relayed.Args.User);
        }

        relayed.Args.Handled = true;
        Dirty(ent);
        SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, relayed.Args.User);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosUiAction(Entity<AtmosControlProgramComponent> ent, ref AtmosControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case AtmosControlAction.SelectTarget:
                if (!string.IsNullOrWhiteSpace(args.Argument))
                {
                    ent.Comp.SelectedTargetGuid = args.Argument.Trim();
                    ent.Comp.StatusMessage = string.Empty;
                    Dirty(ent);
                }
                SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
                UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                return;

            case AtmosControlAction.SelectAccFile:
                ent.Comp.SelectedAccFilePath = string.IsNullOrWhiteSpace(args.Argument) ? null : args.Argument.Trim();
                Dirty(ent);
                UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                return;

            case AtmosControlAction.AuthorizeWithAcc:
                GetAvailableAtmosTargets(compUid, ent.Comp, out var authAlarmUid, out var authAlarmGuid, out var authApcUid, out var authApcGuid);
                if (authAlarmUid == null || string.IsNullOrEmpty(authAlarmGuid))
                {
                    ent.Comp.StatusMessage = "No target Air Alarm / APC found.";
                    UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                    return;
                }

                var reqPath = !string.IsNullOrWhiteSpace(args.Argument) ? args.Argument.Trim() : ent.Comp.SelectedAccFilePath;
                if (!TryFindAccFile(compUid, reqPath, out var accFile))
                {
                    ent.Comp.StatusMessage = "Authorization failed: No .acc file found in /keys/.";
                    UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                    return;
                }

                ent.Comp.SelectedAccFilePath = accFile.FullPath;
                var checkUid = authApcUid ?? authAlarmUid.Value;
                if (CanAccFileAuthorizeDevice(accFile, checkUid, isAtmos: true) ||
                    CanAccFileAuthorizeDevice(accFile, authAlarmUid.Value, isAtmos: true))
                {
                    GrantEngineeringApcAuthorization(compUid, authApcGuid, checkUid, args.Actor, authAlarmGuid, authAlarmUid.Value);
                    ent.Comp.StatusMessage = $"Authorized to {MetaData(authAlarmUid.Value).EntityName} [APC {authApcGuid}] via {accFile.FullPath}.";
                }
                else
                {
                    ent.Comp.StatusMessage = $"Access denied: {accFile.FullPath} lacks required Atmos/Engineering access for {MetaData(authAlarmUid.Value).EntityName}.";
                }

                Dirty(ent);
                SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
                UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                return;

            case AtmosControlAction.RevokeAuth:
                GetAvailableAtmosTargets(compUid, ent.Comp, out _, out var revAlarmGuid, out _, out var revApcGuid);
                ent.Comp.AuthorizedTargetGuids.Remove(revAlarmGuid);
                ent.Comp.AuthorizedTargetGuids.Remove(revApcGuid);
                ent.Comp.StatusMessage = $"Revoked remote session authorization for [{revApcGuid}].";
                Dirty(ent);
                UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
                return;
        }

        GetAvailableAtmosTargets(compUid, ent.Comp, out var targetAlarmUid, out var targetAlarmGuid, out _, out var targetApcGuid);
        var isAuthorized = targetAlarmUid != null &&
                           IsAtmosTargetAuthorized(compUid, ent.Comp, targetAlarmUid.Value, targetAlarmGuid, targetApcGuid);

        if (!isAuthorized || ent.Comp.Locked)
        {
            ent.Comp.StatusMessage = "Action blocked: Air Alarm / APC is LOCKED. Swipe authorized ID card or authorize with a .acc file.";
            SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
            UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
            return;
        }

        switch (args.Action)
        {
            case AtmosControlAction.Refresh:
                SendAtmosDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
                break;
            case AtmosControlAction.SetModeFiltering:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_mode", "Filtering", args.Actor);
                break;
            case AtmosControlAction.SetModeWideFiltering:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_mode", "WideFiltering", args.Actor);
                break;
            case AtmosControlAction.SetModeFill:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_mode", "Fill", args.Actor);
                break;
            case AtmosControlAction.SetModePanic:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_mode", "Panic", args.Actor);
                break;
            case AtmosControlAction.SetModeNone:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_mode", "None", args.Actor);
                break;
            case AtmosControlAction.ToggleAutoMode:
                SendAtmosDeviceLink(compUid, ent.Comp, "set_auto_mode", (!ent.Comp.AutoMode).ToString(), args.Actor);
                break;
            case AtmosControlAction.ResyncDevices:
                SendAtmosDeviceLink(compUid, ent.Comp, "resync_devices", string.Empty, args.Actor);
                break;
            case AtmosControlAction.SaveLog:
                if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
                {
                    var textFile = new TextVirtualFile
                    {
                        Name = "atmos_snapshot",
                        Extension = "txt",
                        Directory = "/logs/",
                        TextContent = $"Alarm: {ent.Comp.AlarmLevel}\nMode: {ent.Comp.CurrentMode} (Auto: {ent.Comp.AutoMode})\nPressure: {ent.Comp.PressureKpa:0.0} kPa | Temp: {ent.Comp.TemperatureCelsius:0.0} C\n{ent.Comp.GasSummary}",
                        Size = 256
                    };
                    _fileSystem.TryWriteFile(driveUid.Value, textFile, fs);
                    ent.Comp.StatusMessage = "Saved Air Alarm telemetry to /logs/atmos_snapshot.txt.";
                }
                else
                {
                    ent.Comp.StatusMessage = "Save failed: No hard drive installed.";
                }
                break;
        }

        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosUpdateVent(Entity<AtmosControlProgramComponent> ent, ref AtmosUpdateVentMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        var dto = new AtmosVentEntryDto(
            args.AddressId,
            string.Empty,
            args.Enabled,
            args.PumpDirection,
            args.PressureChecks,
            args.ExternalPressureBound,
            args.InternalPressureBound);

        var cmd = args.ApplyToAll ? "set_all_vents" : "set_vent";
        var pkt = new ComputerPacket(cmd);
        pkt.Set("VentRaw", AtmosControlCodec.EncodeVents([dto]));
        SendAtmosDeviceLinkPacket(compUid, ent.Comp, cmd, string.Empty, args.Actor, pkt);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosUpdateScrubber(Entity<AtmosControlProgramComponent> ent, ref AtmosUpdateScrubberMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        var dto = new AtmosScrubberEntryDto(
            args.AddressId,
            string.Empty,
            args.Enabled,
            args.PumpDirection,
            args.VolumeRate,
            args.WideNet,
            false,
            args.FilterGases);

        var cmd = args.ApplyToAll ? "set_all_scrubbers" : "set_scrubber";
        var pkt = new ComputerPacket(cmd);
        pkt.Set("ScrubberRaw", AtmosControlCodec.EncodeScrubbers([dto]));
        SendAtmosDeviceLinkPacket(compUid, ent.Comp, cmd, string.Empty, args.Actor, pkt);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosUpdateSensorThreshold(Entity<AtmosControlProgramComponent> ent, ref AtmosUpdateSensorThresholdMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        var cmd = args.ApplyToAll ? "set_all_thresholds" : "set_threshold";
        var pkt = new ComputerPacket(cmd);
        pkt.Set("AddressId", args.AddressId);
        pkt.Set("ThresholdType", (int) args.ThresholdType);
        pkt.Set("Gas", args.Gas is { } g ? (int) g : -1);
        pkt.Set("ThresholdRaw", AtmosControlCodec.EncodeThreshold(args.Threshold));
        SendAtmosDeviceLinkPacket(compUid, ent.Comp, cmd, string.Empty, args.Actor, pkt);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosSaveTemplate(Entity<AtmosControlProgramComponent> ent, ref AtmosSaveTemplateMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        var kind = args.Kind;
        var cleanName = (args.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanName))
        {
            cleanName = kind switch
            {
                AtmosTemplateKind.Vent => "Vent",
                AtmosTemplateKind.Scrubber => "Scrubber",
                _ => "Sensor"
            };
        }

        if (cleanName.Length > 24)
            cleanName = cleanName[..24];

        // Single active template slot: saving replaces any previous template.
        ent.Comp.Templates.Clear();
        ent.Comp.Templates.Add(new AtmosTemplateEntryDto(cleanName, kind, args.Vent, args.Scrubber, args.Sensor));
        Dirty(ent);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosDeleteTemplate(Entity<AtmosControlProgramComponent> ent, ref AtmosDeleteTemplateMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        ent.Comp.Templates.Clear();
        Dirty(ent);
        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnAtmosApplyTemplate(Entity<AtmosControlProgramComponent> ent, ref AtmosApplyTemplateMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid || ent.Comp.Locked)
            return;

        var kind = args.Kind;
        var template = ent.Comp.Templates.FirstOrDefault(t => t.Kind == kind);
        if (template == null)
            return;

        switch (template.Kind)
        {
            case AtmosTemplateKind.Vent when template.Vent is { } v:
                var ventDto = new AtmosVentEntryDto(
                    args.TargetAddressId ?? v.AddressId,
                    string.Empty,
                    v.Enabled,
                    v.PumpDirection,
                    v.PressureChecks,
                    v.ExternalPressureBound,
                    v.InternalPressureBound);
                var ventCmd = args.ApplyToAll ? "set_all_vents" : "set_vent";
                var ventPkt = new ComputerPacket(ventCmd);
                ventPkt.Set("VentRaw", AtmosControlCodec.EncodeVents([ventDto]));
                SendAtmosDeviceLinkPacket(compUid, ent.Comp, ventCmd, string.Empty, args.Actor, ventPkt);
                break;

            case AtmosTemplateKind.Scrubber when template.Scrubber is { } s:
                var scrubDto = new AtmosScrubberEntryDto(
                    args.TargetAddressId ?? s.AddressId,
                    string.Empty,
                    s.Enabled,
                    s.PumpDirection,
                    s.VolumeRate,
                    s.WideNet,
                    false,
                    new List<Gas>(s.FilterGases));
                var scrubCmd = args.ApplyToAll ? "set_all_scrubbers" : "set_scrubber";
                var scrubPkt = new ComputerPacket(scrubCmd);
                scrubPkt.Set("ScrubberRaw", AtmosControlCodec.EncodeScrubbers([scrubDto]));
                SendAtmosDeviceLinkPacket(compUid, ent.Comp, scrubCmd, string.Empty, args.Actor, scrubPkt);
                break;

            case AtmosTemplateKind.Sensor when template.Sensor is { } sens:
                var sensCmd = args.ApplyToAll ? "apply_sensor_template_all" : "apply_sensor_template";
                var sensPkt = new ComputerPacket(sensCmd);
                if (args.TargetAddressId is { } tid)
                    sensPkt.Set("AddressId", tid);
                sensPkt.Set("PressureThreshold", AtmosControlCodec.EncodeThreshold(sens.PressureThreshold));
                sensPkt.Set("TemperatureThreshold", AtmosControlCodec.EncodeThreshold(sens.TemperatureThreshold));
                var gasRaw = string.Join("~", sens.GasThresholds.Select(kv => $"{(int) kv.Key}={AtmosControlCodec.EncodeThreshold(kv.Value)}"));
                sensPkt.Set("GasThresholds", gasRaw);
                SendAtmosDeviceLinkPacket(compUid, ent.Comp, sensCmd, string.Empty, args.Actor, sensPkt);
                break;
        }

        UpdateAtmosUi(ent.Owner, compUid, ent.Comp);
    }

    private void SendAtmosDeviceLink(EntityUid compUid, AtmosControlProgramComponent comp, string cmd, string arg, EntityUid? actor)
    {
        var pkt = new ComputerPacket(cmd);
        pkt.Set("Argument", arg);
        SendAtmosDeviceLinkPacket(compUid, comp, cmd, arg, actor, pkt);
    }

    private void SendAtmosDeviceLinkPacket(EntityUid compUid, AtmosControlProgramComponent comp, string cmd, string arg, EntityUid? actor, ComputerPacket pkt)
    {
        GetAvailableAtmosTargets(compUid, comp, out var targetAlarmUid, out var targetAlarmGuid, out _, out var targetApcGuid);
        var isLocalHostAlarm = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine == targetAlarmUid;

        if (!isLocalHostAlarm && targetAlarmUid is { Valid: true } remoteAlarmUid && Exists(remoteAlarmUid))
        {
            var isAuth = IsAtmosTargetAuthorized(compUid, comp, remoteAlarmUid, targetAlarmGuid, targetApcGuid);
            if (isAuth && cmd is not ("status" or "status_request") && _lock.IsLocked(remoteAlarmUid))
                _lock.Unlock(remoteAlarmUid, actor);

            DispatchPacketToRemoteHost(remoteAlarmUid, "AirAlarmCommandSink", pkt, actor);
            SyncAtmosTelemetryFromTarget(compUid, comp, remoteAlarmUid, targetAlarmGuid, targetApcGuid);
            return;
        }

        var sent = _computer.TryInvokeDeviceLinkPort(compUid, "AirAlarmCommandSink", isHostPort: true, cmd, arg, out var err, actor, pkt);
        if (!sent)
            sent = _computer.TryInvokeDeviceLinkPort(compUid, "MachineCommandReceiver", isHostPort: true, cmd, arg, out err, actor, pkt);

        if (!sent && targetAlarmUid == null)
            comp.StatusMessage = $"DeviceLink error: {err}";
    }

    private void UpdateAtmosUi(EntityUid progUid, EntityUid compUid, AtmosControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
        var isRemoteConsole = cComp?.HostMachine == null || !_airAlarmQuery.HasComp(cComp.HostMachine.Value);
        var targets = GetAvailableAtmosTargets(compUid, comp, out var selectedAlarmUid, out var selectedAlarmGuid, out _, out var selectedApcGuid);
        var accFiles = GetAvailableAccFilePaths(compUid, ref comp.SelectedAccFilePath);
        var isAuthorized = selectedAlarmUid != null &&
                           IsAtmosTargetAuthorized(compUid, comp, selectedAlarmUid.Value, selectedAlarmGuid, selectedApcGuid);

        if (selectedAlarmUid != null && (isRemoteConsole || !comp.HasTelemetry))
        {
            SyncAtmosTelemetryFromTarget(compUid, comp, selectedAlarmUid.Value, selectedAlarmGuid, selectedApcGuid);
            isAuthorized = IsAtmosTargetAuthorized(compUid, comp, selectedAlarmUid.Value, selectedAlarmGuid, selectedApcGuid);
        }
        else if (selectedAlarmUid != null)
        {
            comp.Locked = !isAuthorized;
        }

        var state = new AtmosControlUiState(
            comp.HasTelemetry,
            dlEnabled,
            comp.AlarmLevel,
            comp.CurrentMode,
            comp.AutoMode,
            comp.Locked,
            comp.PressureKpa,
            comp.TemperatureCelsius,
            comp.VentCount,
            comp.ScrubberCount,
            comp.SensorCount,
            comp.GasSummary,
            comp.StatusMessage,
            new List<AtmosVentEntryDto>(comp.Vents),
            new List<AtmosScrubberEntryDto>(comp.Scrubbers),
            new List<AtmosSensorEntryDto>(comp.Sensors),
            new List<AtmosTemplateEntryDto>(comp.Templates),
            selectedAlarmGuid,
            selectedApcGuid,
            isAuthorized,
            comp.SelectedAccFilePath,
            accFiles,
            targets,
            isRemoteConsole);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }

    // =========================================================================
    // PER-APC / PER-DEVICE AUTHORIZATION & REMOTE DISCOVERY HELPERS
    // =========================================================================

    private string GetDeviceOrHostGuid(EntityUid deviceUid)
    {
        if (TryComp<EmbeddedComputerHostComponent>(deviceUid, out var host) &&
            _computer.EnsureComputerBoundToHost((deviceUid, host), out var compUid))
        {
            return _router.EnsureDeviceGuid(compUid);
        }

        return _router.EnsureDeviceGuid(deviceUid);
    }

    private EntityUid? ResolveGoverningApc(EntityUid alarmUid)
    {
        if (TryComp<ApcPowerReceiverComponent>(alarmUid, out var recv) &&
            recv.Provider is { } provider &&
            Exists(provider.Owner) &&
            _apcQuery.HasComp(provider.Owner))
        {
            return provider.Owner;
        }

        var alarmXform = Transform(alarmUid);
        EntityUid? bestApc = null;
        var bestDistSq = float.MaxValue;
        var alarmCoords = _transform.GetMapCoordinates(alarmUid, alarmXform);

        var q = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (q.MoveNext(out var apcUid, out _, out var apcXform))
        {
            if (apcXform.MapID != alarmXform.MapID)
                continue;
            if (alarmXform.GridUid != null && apcXform.GridUid != null && apcXform.GridUid != alarmXform.GridUid)
                continue;

            var dSq = (_transform.GetMapCoordinates(apcUid, apcXform).Position - alarmCoords.Position).LengthSquared();
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestApc = apcUid;
            }
        }

        return bestApc;
    }

    private List<RemoteApcTargetDto> GetAvailableApcTargets(
        EntityUid compUid,
        ApcControlProgramComponent comp,
        out EntityUid? selectedApcUid,
        out string selectedGuid)
    {
        selectedApcUid = null;
        selectedGuid = string.Empty;

        var anchorUid = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } hm ? hm : compUid;
        var anchorXform = Transform(anchorUid);
        var entries = new List<(EntityUid Uid, string Guid, RemoteApcTargetDto Dto)>();
        var seen = new HashSet<EntityUid>();

        void AddApcCandidate(EntityUid apcUid, ApcComponent apcComp)
        {
            if (!seen.Add(apcUid))
                return;

            var guid = GetDeviceOrHostGuid(apcUid);
            var isAuth = IsApcTargetAuthorized(compUid, comp, apcUid, guid);
            var pct = 0f;
            var load = 0;
            if (TryComp<PowerNetworkBatteryComponent>(apcUid, out var netBat) && netBat.NetworkBattery.Capacity > 0)
            {
                pct = Math.Clamp(netBat.NetworkBattery.CurrentStorage / netBat.NetworkBattery.Capacity * 100f, 0f, 100f);
                load = (int) MathF.Ceiling(netBat.NetworkBattery.CurrentSupply);
            }

            entries.Add((apcUid, guid, new RemoteApcTargetDto(
                guid,
                MetaData(apcUid).EntityName,
                isAuth,
                apcComp.MainBreakerEnabled,
                pct,
                load)));
        }

        if (cComp?.HostMachine is { } hostApc && _apcQuery.TryComp(hostApc, out var hostApcComp))
            AddApcCandidate(hostApc, hostApcComp);

        var q = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (q.MoveNext(out var apcUid, out var apcComp, out var xform))
        {
            if (xform.MapID != anchorXform.MapID)
                continue;
            if (anchorXform.GridUid != null && xform.GridUid != null && xform.GridUid != anchorXform.GridUid)
                continue;

            AddApcCandidate(apcUid, apcComp);
        }

        if (entries.Count == 0)
            return new List<RemoteApcTargetDto>();

        var match = entries.FirstOrDefault(e => string.Equals(e.Guid, comp.SelectedTargetGuid, StringComparison.OrdinalIgnoreCase));
        if (match.Uid == EntityUid.Invalid)
        {
            match = entries[0];
            comp.SelectedTargetGuid = match.Guid;
        }

        selectedApcUid = match.Uid;
        selectedGuid = match.Guid;
        return entries.Select(e => e.Dto).ToList();
    }

    private List<RemoteAtmosTargetDto> GetAvailableAtmosTargets(
        EntityUid compUid,
        AtmosControlProgramComponent comp,
        out EntityUid? selectedAlarmUid,
        out string selectedAlarmGuid,
        out EntityUid? selectedApcUid,
        out string selectedApcGuid)
    {
        selectedAlarmUid = null;
        selectedAlarmGuid = string.Empty;
        selectedApcUid = null;
        selectedApcGuid = string.Empty;

        var anchorUid = _computerQuery.TryComp(compUid, out var cComp) && cComp.HostMachine is { } hm ? hm : compUid;
        var anchorXform = Transform(anchorUid);
        var entries = new List<(EntityUid AlarmUid, string AlarmGuid, EntityUid? ApcUid, string ApcGuid, RemoteAtmosTargetDto Dto)>();
        var seen = new HashSet<EntityUid>();

        void AddAlarmCandidate(EntityUid alarmUid)
        {
            if (!seen.Add(alarmUid))
                return;

            var alarmGuid = GetDeviceOrHostGuid(alarmUid);
            var apcUid = ResolveGoverningApc(alarmUid);
            var apcGuid = apcUid != null ? GetDeviceOrHostGuid(apcUid.Value) : alarmGuid;
            var isAuth = IsAtmosTargetAuthorized(compUid, comp, alarmUid, alarmGuid, apcGuid);

            var level = "Normal";
            var pres = 101.3f;
            var temp = 20f;
            if (TryComp<EmbeddedComputerHostComponent>(alarmUid, out var host) &&
                host.ComputerEntity is { } alarmCompUid &&
                _computerQuery.TryComp(alarmCompUid, out var alarmComp))
            {
                foreach (var pUid in alarmComp.RunningPrograms)
                {
                    if (TryComp<AtmosControlProgramComponent>(pUid, out var embeddedAtmos) && embeddedAtmos.HasTelemetry)
                    {
                        level = embeddedAtmos.AlarmLevel;
                        pres = embeddedAtmos.PressureKpa;
                        temp = embeddedAtmos.TemperatureCelsius;
                        break;
                    }
                }
            }

            var areaLabel = apcUid != null
                ? $"{MetaData(alarmUid).EntityName} ({MetaData(apcUid.Value).EntityName})"
                : MetaData(alarmUid).EntityName;

            entries.Add((alarmUid, alarmGuid, apcUid, apcGuid, new RemoteAtmosTargetDto(
                alarmGuid,
                apcGuid,
                areaLabel,
                isAuth,
                level,
                pres,
                temp)));
        }

        if (cComp?.HostMachine is { } hostAlarm && _airAlarmQuery.HasComp(hostAlarm))
            AddAlarmCandidate(hostAlarm);

        var q = EntityQueryEnumerator<AirAlarmComponent, TransformComponent>();
        while (q.MoveNext(out var alarmUid, out _, out var xform))
        {
            if (xform.MapID != anchorXform.MapID)
                continue;
            if (anchorXform.GridUid != null && xform.GridUid != null && xform.GridUid != anchorXform.GridUid)
                continue;

            AddAlarmCandidate(alarmUid);
        }

        if (entries.Count == 0)
            return new List<RemoteAtmosTargetDto>();

        var match = entries.FirstOrDefault(e =>
            string.Equals(e.AlarmGuid, comp.SelectedTargetGuid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.ApcGuid, comp.SelectedTargetGuid, StringComparison.OrdinalIgnoreCase));
        if (match.AlarmUid == EntityUid.Invalid)
        {
            match = entries[0];
            comp.SelectedTargetGuid = match.AlarmGuid;
        }

        selectedAlarmUid = match.AlarmUid;
        selectedAlarmGuid = match.AlarmGuid;
        selectedApcUid = match.ApcUid;
        selectedApcGuid = match.ApcGuid;
        return entries.Select(e => e.Dto).ToList();
    }

    private bool IsApcTargetAuthorized(EntityUid compUid, ApcControlProgramComponent comp, EntityUid targetApcUid, string targetGuid)
    {
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine == targetApcUid &&
            !_lock.IsLocked(targetApcUid))
        {
            return true;
        }

        return !string.IsNullOrEmpty(targetGuid) && comp.AuthorizedTargetGuids.Contains(targetGuid);
    }

    private bool IsAtmosTargetAuthorized(
        EntityUid compUid,
        AtmosControlProgramComponent comp,
        EntityUid targetAlarmUid,
        string targetAlarmGuid,
        string apcGuid)
    {
        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine == targetAlarmUid &&
            !_lock.IsLocked(targetAlarmUid))
        {
            return true;
        }

        return (!string.IsNullOrEmpty(apcGuid) && comp.AuthorizedTargetGuids.Contains(apcGuid)) ||
               (!string.IsNullOrEmpty(targetAlarmGuid) && comp.AuthorizedTargetGuids.Contains(targetAlarmGuid));
    }

    private void GrantEngineeringApcAuthorization(
        EntityUid compUid,
        string apcGuid,
        EntityUid apcOrDeviceUid,
        EntityUid? actor,
        string? secondaryGuid = null,
        EntityUid? secondaryDeviceUid = null)
    {
        if (Exists(apcOrDeviceUid) && HasComp<LockComponent>(apcOrDeviceUid) && _lock.IsLocked(apcOrDeviceUid))
            _lock.Unlock(apcOrDeviceUid, actor);

        if (secondaryDeviceUid is { Valid: true } secUid && Exists(secUid) && HasComp<LockComponent>(secUid) && _lock.IsLocked(secUid))
            _lock.Unlock(secUid, actor);

        if (!_computerQuery.TryComp(compUid, out var cComp))
            return;

        foreach (var progUid in cComp.RunningPrograms)
        {
            if (TryComp<ApcControlProgramComponent>(progUid, out var apcProg))
            {
                if (!string.IsNullOrEmpty(apcGuid))
                    apcProg.AuthorizedTargetGuids.Add(apcGuid);
                if (!string.IsNullOrEmpty(secondaryGuid))
                    apcProg.AuthorizedTargetGuids.Add(secondaryGuid);
                Dirty(progUid, apcProg);
            }

            if (TryComp<AtmosControlProgramComponent>(progUid, out var atmosProg))
            {
                if (!string.IsNullOrEmpty(apcGuid))
                    atmosProg.AuthorizedTargetGuids.Add(apcGuid);
                if (!string.IsNullOrEmpty(secondaryGuid))
                    atmosProg.AuthorizedTargetGuids.Add(secondaryGuid);
                Dirty(progUid, atmosProg);
            }
        }
    }

    private bool TryExtractIdCard(EntityUid used, out EntityUid cardUid)
    {
        if (_idCardQuery.HasComp(used))
        {
            cardUid = used;
            return true;
        }

        if (TryComp<PdaComponent>(used, out var pda))
        {
            var pdaCard = pda.IdSlot.Item ?? pda.ContainedId;
            if (pdaCard is { Valid: true } pId && Exists(pId) && _idCardQuery.HasComp(pId))
            {
                cardUid = pId;
                return true;
            }
        }

        cardUid = EntityUid.Invalid;
        return false;
    }

    private bool CanIdCardAuthorizeDevice(EntityUid cardUid, EntityUid targetDeviceUid, bool isAtmos)
    {
        if (TryComp<AccessReaderComponent>(targetDeviceUid, out var reader) && reader.AccessLists.Count > 0)
            return _accessReader.IsAllowed(cardUid, targetDeviceUid, reader);

        var tags = _accessReader.FindAccessTags(cardUid);
        return tags.Contains(EngineeringAccess) ||
               tags.Contains(CommandAccess) ||
               (isAtmos && tags.Contains(AtmosphericsAccess));
    }

    private bool CanAccFileAuthorizeDevice(AccessCodesVirtualFile accFile, EntityUid targetDeviceUid, bool isAtmos)
    {
        if (accFile.IsEncrypted)
            return false;

        if (TryComp<AccessReaderComponent>(targetDeviceUid, out var reader) && reader.AccessLists.Count > 0)
            return _accessReader.IsAllowed(accFile.AccessTags, Array.Empty<StationRecordKey>(), targetDeviceUid, reader);

        return accFile.AccessTags.Contains(EngineeringAccess) ||
               accFile.AccessTags.Contains(CommandAccess) ||
               (isAtmos && accFile.AccessTags.Contains(AtmosphericsAccess));
    }

    private List<string> GetAvailableAccFilePaths(EntityUid compUid, ref string? selectedPath)
    {
        var paths = new List<string>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
        {
            foreach (var f in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(driveUid.Value, fs).OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(f.FullPath);
        }

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            foreach (var f in _fileSystem.GetFilesByType<AccessCodesVirtualFile>(diskUid, diskFs).OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                if (!paths.Contains(f.FullPath))
                    paths.Add(f.FullPath);
            }
        }

        if (string.IsNullOrEmpty(selectedPath) && paths.Count > 0)
            selectedPath = paths[0];

        return paths;
    }

    private bool TryFindAccFile(EntityUid compUid, string? requestedPath, out AccessCodesVirtualFile accFile)
    {
        var all = new List<AccessCodesVirtualFile>();
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) && driveUid != null && fs != null)
            all.AddRange(_fileSystem.GetFilesByType<AccessCodesVirtualFile>(driveUid.Value, fs));

        if (_itemSlots.TryGetSlot(compUid, ModularComputerComponent.SlotDiskModule, out var diskSlot) &&
            diskSlot.Item is { Valid: true } diskUid &&
            TryComp<FileSystemComponent>(diskUid, out var diskFs))
        {
            all.AddRange(_fileSystem.GetFilesByType<AccessCodesVirtualFile>(diskUid, diskFs));
        }

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var match = all.FirstOrDefault(f =>
                string.Equals(f.FullPath, requestedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Name, requestedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"{f.Name}.{f.Extension}", requestedPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                accFile = match;
                return true;
            }
        }

        if (all.Count > 0)
        {
            accFile = all.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).First();
            return true;
        }

        accFile = default!;
        return false;
    }

    private void DispatchPacketToRemoteHost(EntityUid remoteHostUid, string sinkPortId, ComputerPacket packet, EntityUid? actor)
    {
        if (TryComp<EmbeddedComputerHostComponent>(remoteHostUid, out var host))
            _computer.EnsureComputerBoundToHost((remoteHostUid, host), out _);

        if (actor is { Valid: true } act)
            packet.SetReference("Actor", act);

        var sig = new SignalReceivedEvent<ComputerPacket>(new ProtoId<SinkPortPrototype>(sinkPortId), packet, actor);
        RaiseLocalEvent(remoteHostUid, ref sig);
    }

    private void SyncApcTelemetryFromTarget(EntityUid compUid, ApcControlProgramComponent comp, EntityUid targetApcUid, string targetGuid)
    {
        if (!_apcQuery.TryComp(targetApcUid, out var apcComp))
            return;

        var isAuth = IsApcTargetAuthorized(compUid, comp, targetApcUid, targetGuid);
        var pct = 0f;
        var load = 0;
        if (TryComp<PowerNetworkBatteryComponent>(targetApcUid, out var netBat) && netBat.NetworkBattery.Capacity > 0)
        {
            pct = Math.Clamp(netBat.NetworkBattery.CurrentStorage / netBat.NetworkBattery.Capacity * 100f, 0f, 100f);
            load = (int) MathF.Ceiling(netBat.NetworkBattery.CurrentSupply);
        }

        comp.HasTelemetry = true;
        comp.AreaName = MetaData(targetApcUid).EntityName;
        comp.MainBreakerEnabled = apcComp.MainBreakerEnabled;
        comp.BreakerTripped = apcComp.TripFlag;
        comp.Locked = !isAuth;
        comp.ChargePercent = pct;
        comp.LoadWatts = load;
        comp.ExternalPowerState = apcComp.LastExternalState.ToString();
    }

    private void SyncAtmosTelemetryFromTarget(
        EntityUid compUid,
        AtmosControlProgramComponent comp,
        EntityUid targetAlarmUid,
        string targetAlarmGuid,
        string targetApcGuid)
    {
        if (!Exists(targetAlarmUid))
            return;

        if (TryComp<EmbeddedComputerHostComponent>(targetAlarmUid, out var host) &&
            _computer.EnsureComputerBoundToHost((targetAlarmUid, host), out var alarmCompUid) &&
            _computerQuery.TryComp(alarmCompUid, out var alarmComp))
        {
            // Ensure status packet has been emitted to the alarm's embedded program
            var statusPkt = new ComputerPacket("status");
            var sig = new SignalReceivedEvent<ComputerPacket>(new ProtoId<SinkPortPrototype>("AirAlarmCommandSink"), statusPkt, null);
            RaiseLocalEvent(targetAlarmUid, ref sig);

            foreach (var pUid in alarmComp.RunningPrograms)
            {
                if (!TryComp<AtmosControlProgramComponent>(pUid, out var embedded) || pUid == comp.Owner)
                    continue;

                comp.HasTelemetry = embedded.HasTelemetry;
                comp.AlarmLevel = embedded.AlarmLevel;
                comp.CurrentMode = embedded.CurrentMode;
                comp.AutoMode = embedded.AutoMode;
                comp.PressureKpa = embedded.PressureKpa;
                comp.TemperatureCelsius = embedded.TemperatureCelsius;
                comp.VentCount = embedded.VentCount;
                comp.ScrubberCount = embedded.ScrubberCount;
                comp.SensorCount = embedded.SensorCount;
                comp.GasSummary = embedded.GasSummary;
                comp.Vents = new List<AtmosVentEntryDto>(embedded.Vents);
                comp.Scrubbers = new List<AtmosScrubberEntryDto>(embedded.Scrubbers);
                comp.Sensors = new List<AtmosSensorEntryDto>(embedded.Sensors);
                break;
            }
        }

        comp.Locked = !IsAtmosTargetAuthorized(compUid, comp, targetAlarmUid, targetAlarmGuid, targetApcGuid);
    }

    // =========================================================================
    // AUTOLATHE CONTROL
    // =========================================================================

    private void OnLatheStartup(Entity<LatheControlProgramComponent> ent, ref ProgramStartupEvent args)
    {
        SendLatheDeviceLink(args.ComputerUid, ent.Comp, "status", string.Empty, null);
        UpdateLatheUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnLatheActivated(Entity<LatheControlProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        SendLatheDeviceLink(args.ComputerUid, ent.Comp, "status", string.Empty, null);
        UpdateLatheUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnLatheBuiOpened(Entity<LatheControlProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        SendLatheDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
        UpdateLatheUi(ent.Owner, compUid, ent.Comp);
    }

    private bool TryGetInsertedDisk(
        EntityUid compUid,
        LatheControlProgramComponent comp,
        out EntityUid diskUid,
        out FileSystemComponent? diskFs,
        out string? diskName)
    {
        diskUid = EntityUid.Invalid;
        diskFs = null;
        diskName = null;

        if (_computerQuery.TryComp(compUid, out var cComp) &&
            cComp.HostMachine is { } hostUid &&
            TryComp<LatheComponent>(hostUid, out var lathe))
        {
            if (lathe.diskSlot?.Item is { } hostDisk &&
                !TerminatingOrDeleted(hostDisk) &&
                TryComp<FileSystemComponent>(hostDisk, out diskFs))
            {
                diskUid = hostDisk;
                diskName = MetaData(hostDisk).EntityName;
                return true;
            }

            return false;
        }

        if (comp.InsertedDiskUid is { } linkedDisk &&
            Exists(linkedDisk) &&
            !TerminatingOrDeleted(linkedDisk) &&
            TryComp<FileSystemComponent>(linkedDisk, out diskFs))
        {
            diskUid = linkedDisk;
            diskName = comp.InsertedDiskName ?? MetaData(linkedDisk).EntityName;
            return true;
        }

        return false;
    }

    private void OnDriveFileSystemChanged(Entity<FileSystemComponent> ent, ref FileSystemChangedEvent args)
    {
        var query = EntityQueryEnumerator<LatheControlProgramComponent, ProgramComponent>();
        while (query.MoveNext(out var progUid, out var latheProg, out var prog))
        {
            if (prog.Computer is not { } compUid)
                continue;

            var isOwnHdd = _fileSystem.TryFindFileSystem(compUid, out var driveUid, out _) && driveUid == ent.Owner;
            var isInsertedDisk = TryGetInsertedDisk(compUid, latheProg, out var diskUid, out _, out _) && diskUid == ent.Owner;
            if (isOwnHdd || isInsertedDisk)
                UpdateLatheUi(progUid, compUid, latheProg);
        }
    }

    private void OnLatheDeviceLink(Entity<LatheControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "AutolatheStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.LatheStatus = pkt.GetString("Status", "Standby") ?? "Standby";
        ent.Comp.CurrentPrinting = pkt.GetString("Recipe", string.Empty) ?? string.Empty;
        ent.Comp.SelectedRecipeId = pkt.GetString("SelectedRecipeId", string.Empty) ?? string.Empty;
        ent.Comp.SelectedRecipeFilePath = pkt.GetString("SelectedRecipeFilePath", string.Empty) ?? string.Empty;
        ent.Comp.SelectedRecipeFromDisk = pkt.GetString("SelectedRecipeFromDisk", "False") == "True";

        var startTicksRaw = pkt.GetString("ProdStartTicks", "0");
        var lenTicksRaw = pkt.GetString("ProdLengthTicks", "0");
        ent.Comp.ProductionStartTime = long.TryParse(startTicksRaw, out var sTicks) && sTicks > 0
            ? TimeSpan.FromTicks(sTicks)
            : null;
        ent.Comp.ProductionLength = long.TryParse(lenTicksRaw, out var lTicks) && lTicks > 0
            ? TimeSpan.FromTicks(lTicks)
            : null;

        var multRaw = pkt.GetString("MaterialMultiplier", "1");
        ent.Comp.MaterialUseMultiplier = float.TryParse(multRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMult) && parsedMult > 0f
            ? parsedMult
            : 1f;

        var matRaw = pkt.GetString("MaterialsRaw", string.Empty);
        ent.Comp.Materials.Clear();
        if (!string.IsNullOrEmpty(matRaw))
        {
            foreach (var entry in matRaw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = entry.Split('=', 2);
                if (kv.Length == 2 && int.TryParse(kv[1], out var amt))
                    ent.Comp.Materials[kv[0]] = amt;
            }
        }

        var queueRaw = pkt.GetString("QueueRaw", string.Empty);
        ent.Comp.QueueRecipes = string.IsNullOrEmpty(queueRaw)
            ? new List<string>()
            : queueRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        ent.Comp.QueueEntries.Clear();
        var queueBatchesRaw = pkt.GetString("QueueBatchesRaw", string.Empty);
        if (!string.IsNullOrEmpty(queueBatchesRaw))
        {
            foreach (var entry in queueBatchesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = entry.Split('=', 2);
                if (kv.Length == 2 && int.TryParse(kv[1], out var count) && count > 0)
                    ent.Comp.QueueEntries.Add(new LatheQueueEntryDto(kv[0], count));
            }
        }
        else
        {
            foreach (var rId in ent.Comp.QueueRecipes)
                ent.Comp.QueueEntries.Add(new LatheQueueEntryDto(rId, 1));
        }

        var recRaw = pkt.GetString("HostRecipesRaw", string.Empty);
        ent.Comp.HostRecipes = string.IsNullOrEmpty(recRaw)
            ? new List<string>()
            : recRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var diskName = pkt.GetString("DiskName", string.Empty);
        ent.Comp.InsertedDiskName = string.IsNullOrEmpty(diskName) ? null : diskName;
        ent.Comp.InsertedDiskUid = pkt.ProtectedReferences.TryGetValue("DiskUid", out var pktDiskUid) && Exists(pktDiskUid)
            ? pktDiskUid
            : null;
        ent.Comp.StatusMessage = pkt.GetString("StatusMessage", string.Empty) ?? string.Empty;
        Dirty(ent);

        UpdateLatheUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnLatheUiAction(Entity<LatheControlProgramComponent> ent, ref LatheControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        switch (args.Action)
        {
            case LatheControlAction.Refresh:
                SendLatheDeviceLink(compUid, ent.Comp, "status", string.Empty, args.Actor);
                break;

            case LatheControlAction.CancelAll:
                SendLatheDeviceLink(compUid, ent.Comp, "cancel", string.Empty, args.Actor);
                break;

            case LatheControlAction.EjectMaterial:
                var ejectPkt = new ComputerPacket("eject_material");
                ejectPkt.Set("Material", args.RecipeId);
                ejectPkt.Set("Count", Math.Max(1, args.Amount));
                _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheCommandSink", isHostPort: true, "eject_material", args.RecipeId, out _, args.Actor, ejectPkt);
                break;

            case LatheControlAction.EjectDisk:
                var ejectDiskPkt = new ComputerPacket("eject_disk");
                ejectDiskPkt.SetReference("Actor", args.Actor);
                _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheCommandSink", isHostPort: true, "eject_disk", string.Empty, out _, args.Actor, ejectDiskPkt);
                break;

            case LatheControlAction.DecryptBlueprint:
                ent.Comp.StatusMessage = "Encrypted recipe files are copy-protected to their physical disk and cannot be decrypted. Print directly from the inserted disk.";
                break;

            case LatheControlAction.ImportDiskBlueprints:
                var importPkt = new ComputerPacket("sync_disk_to_host");
                if (!string.IsNullOrWhiteSpace(args.FilePath))
                    importPkt.Set("FilePath", args.FilePath);
                _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheBlueprintSink", isHostPort: true, "import_disk", args.FilePath, out _, args.Actor, importPkt);
                break;

            case LatheControlAction.SelectRecipe:
                var selectPkt = new ComputerPacket("set_recipe");
                var selectedId = (args.RecipeId ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(selectedId))
                {
                    EntityUid? selDriveUid = null;
                    RecipeBlueprintFile? selBp = null;
                    var selPath = args.FilePath;
                    var selFromDisk = false;

                    if (args.FromDisk)
                    {
                        if (TryGetInsertedDisk(compUid, ent.Comp, out var diskUid, out var diskFs, out _) && diskFs != null)
                        {
                            if (!string.IsNullOrEmpty(selPath))
                                _fileSystem.TryGetFile(diskUid, selPath, out selBp, diskFs);

                            if (selBp == null)
                            {
                                foreach (var (fPath, f) in diskFs.Files)
                                {
                                    if (f is RecipeBlueprintFile candidate &&
                                        candidate.GetAllRecipes().Any(r => r.Id == selectedId))
                                    {
                                        selBp = candidate;
                                        selPath = fPath;
                                        break;
                                    }
                                }
                            }

                            if (selBp != null)
                            {
                                selDriveUid = diskUid;
                                selFromDisk = true;
                            }
                        }
                    }
                    else if (_fileSystem.TryFindFileSystem(compUid, out var hddUid, out var hddFs))
                    {
                        if (!string.IsNullOrEmpty(selPath))
                            _fileSystem.TryGetFile(hddUid.Value, selPath, out selBp, hddFs);

                        if (selBp == null)
                        {
                            foreach (var (fPath, f) in hddFs.Files)
                            {
                                if (f is RecipeBlueprintFile candidate &&
                                    !candidate.IsEncrypted &&
                                    candidate.GetAllRecipes().Any(r => r.Id == selectedId))
                                {
                                    selBp = candidate;
                                    selPath = fPath;
                                    break;
                                }
                            }
                        }

                        if (selBp != null && !selBp.IsEncrypted)
                            selDriveUid = hddUid.Value;
                    }

                    if (selBp == null || selDriveUid == null)
                        break;

                    selectPkt.Set("Recipe", selectedId);
                    selectPkt.Set("FilePath", selPath);
                    selectPkt.Set("FromDisk", selFromDisk.ToString());
                    selectPkt.SetReference("DriveUid", selDriveUid.Value);
                }
                else
                {
                    selectPkt.Set("Recipe", string.Empty);
                }

                if (!_computer.TryInvokeDeviceLinkPort(compUid, "AutolatheSetRecipeSink", isHostPort: true, "set_recipe", selectedId, out _, args.Actor, selectPkt))
                    _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheCommandSink", isHostPort: true, "set_recipe", selectedId, out _, args.Actor, selectPkt);
                break;

            case LatheControlAction.PrintRecipe:
                var amount = Math.Max(1, args.Amount);
                var requestedRecipeId = args.RecipeId;
                var pointCost = _proto.TryIndex<LatheRecipePrototype>(requestedRecipeId, out var reqProto) && reqProto.pointUsage > 0
                    ? reqProto.pointUsage
                    : 1;
                var requiredUses = pointCost * amount;

                var pkt = new ComputerPacket("lathe_print");
                pkt.Set("Recipe", requestedRecipeId);
                pkt.Set("Amount", amount);

                EntityUid? targetDriveUid = null;
                RecipeBlueprintFile? bpFile = null;
                var resolvedPath = args.FilePath;
                var printingFromDisk = false;

                if (args.FromDisk)
                {
                    if (!TryGetInsertedDisk(compUid, ent.Comp, out var diskUid, out var diskFs, out _) || diskFs == null)
                    {
                        ent.Comp.StatusMessage = "Cannot print: recipe disk is no longer inserted.";
                        break;
                    }

                    if (!string.IsNullOrEmpty(resolvedPath))
                        _fileSystem.TryGetFile(diskUid, resolvedPath, out bpFile, diskFs);

                    if (bpFile == null)
                    {
                        foreach (var (fPath, f) in diskFs.Files)
                        {
                            if (f is RecipeBlueprintFile candidate &&
                                candidate.GetAllRecipes().Any(r => r.Id == requestedRecipeId))
                            {
                                bpFile = candidate;
                                resolvedPath = fPath;
                                break;
                            }
                        }
                    }

                    targetDriveUid = diskUid;
                    printingFromDisk = true;
                }
                else
                {
                    // Search embedded HDD memory first, then fall back to inserted disk if present.
                    if (_fileSystem.TryFindFileSystem(compUid, out var bpDrive, out var bpFs))
                    {
                        if (!string.IsNullOrEmpty(resolvedPath))
                            _fileSystem.TryGetFile(bpDrive.Value, resolvedPath, out bpFile, bpFs);

                        if (bpFile == null)
                        {
                            foreach (var (fPath, f) in bpFs.Files)
                            {
                                if (f is RecipeBlueprintFile candidate &&
                                    !candidate.IsEncrypted &&
                                    candidate.GetAllRecipes().Any(r => r.Id == requestedRecipeId))
                                {
                                    bpFile = candidate;
                                    resolvedPath = fPath;
                                    break;
                                }
                            }
                        }

                        if (bpFile != null)
                            targetDriveUid = bpDrive.Value;
                    }

                    if (bpFile == null &&
                        TryGetInsertedDisk(compUid, ent.Comp, out var fallbackDiskUid, out var fallbackDiskFs, out _) &&
                        fallbackDiskFs != null)
                    {
                        if (!string.IsNullOrEmpty(resolvedPath))
                            _fileSystem.TryGetFile(fallbackDiskUid, resolvedPath, out bpFile, fallbackDiskFs);

                        if (bpFile == null)
                        {
                            foreach (var (fPath, f) in fallbackDiskFs.Files)
                            {
                                if (f is RecipeBlueprintFile candidate &&
                                    candidate.GetAllRecipes().Any(r => r.Id == requestedRecipeId))
                                {
                                    bpFile = candidate;
                                    resolvedPath = fPath;
                                    break;
                                }
                            }
                        }

                        if (bpFile != null)
                        {
                            targetDriveUid = fallbackDiskUid;
                            printingFromDisk = true;
                        }
                    }
                }

                if (bpFile == null || targetDriveUid == null)
                {
                    ent.Comp.StatusMessage = $"Blueprint for '{requestedRecipeId}' not found in lathe memory or inserted disk.";
                    break;
                }

                if (bpFile.IsEncrypted && !printingFromDisk)
                {
                    ent.Comp.StatusMessage = $"Encrypted recipe file '{resolvedPath}' can only be printed directly from a recipe disk.";
                    break;
                }

                var requiresUses = bpFile.IsEncrypted || bpFile.DrmLocked || bpFile.RemainingUses != null || pointCost > 1;
                if (requiresUses && (bpFile.RemainingUses == null || bpFile.RemainingUses.Value < requiredUses))
                {
                    ent.Comp.StatusMessage = $"Insufficient uses on '{bpFile.Name}.{bpFile.Extension}' (needs {requiredUses} LP, {bpFile.RemainingUses ?? 0} remaining).";
                    break;
                }

                pkt.SetReference("DriveUid", targetDriveUid.Value);
                pkt.Set("FilePath", resolvedPath);
                if (!_computer.TryInvokeDeviceLinkPort(compUid, "AutolatheBlueprintSink", isHostPort: true, "blueprint_print", requestedRecipeId, out var printErr, args.Actor, pkt))
                    ent.Comp.StatusMessage = $"DeviceLink error: {printErr}";
                break;
        }

        UpdateLatheUi(ent.Owner, compUid, ent.Comp);
    }

    private void SendLatheDeviceLink(EntityUid compUid, LatheControlProgramComponent comp, string cmd, string arg, EntityUid? actor)
    {
        var pkt = new ComputerPacket(cmd);
        pkt.Set("Argument", arg);
        var sent = _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheCommandSink", isHostPort: true, cmd, arg, out var err, actor, pkt);
        if (!sent)
            sent = _computer.TryInvokeDeviceLinkPort(compUid, "MachineCommandReceiver", isHostPort: true, cmd, arg, out err, actor, pkt);

        if (!sent)
            comp.StatusMessage = $"DeviceLink error: {err}";
    }

    private void AddBlueprintsFromFileSystem(
        FileSystemComponent fs,
        bool fromDisk,
        List<LatheBlueprintEntryDto> targetList,
        HashSet<(bool FromDisk, string RecipeId, string FilePath)> seen)
    {
        foreach (var (path, file) in fs.Files)
        {
            if (file is not RecipeBlueprintFile bp)
                continue;

            // Encrypted recipe blueprints only exist as a concept on physical recipe disks.
            if (!fromDisk && bp.IsEncrypted)
                continue;

            var fileSize = bp.CalculateSize(_proto);
            foreach (var recId in bp.GetAllRecipes())
            {
                if (!seen.Add((fromDisk, recId.Id, path)))
                    continue;

                var recName = recId.Id;
                var pointCost = 1;
                if (_proto.TryIndex<LatheRecipePrototype>(recId, out var rProto))
                {
                    if (!string.IsNullOrWhiteSpace(rProto.Name))
                        recName = Loc.GetString(rProto.Name);
                    else if (rProto.Result is { } res && _proto.TryIndex(res, out var entProto))
                        recName = entProto.Name;

                    if (rProto.pointUsage > 0)
                        pointCost = rProto.pointUsage;
                }

                targetList.Add(new LatheBlueprintEntryDto(
                    recId,
                    recName,
                    path,
                    bp.RemainingUses,
                    bp.DrmLocked,
                    bp.IsEncrypted,
                    fromDisk,
                    fileSize,
                    pointCost));
            }
        }
    }

    private void UpdateLatheUi(EntityUid progUid, EntityUid compUid, LatheControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
        var recipes = new List<LatheBlueprintEntryDto>();
        var seen = new HashSet<(bool FromDisk, string RecipeId, string FilePath)>();

        // 1. Instantly load all designs from any inserted disk (encrypted or unencrypted).
        var hasDisk = TryGetInsertedDisk(compUid, comp, out _, out var diskFs, out var liveDiskName);
        if (hasDisk && diskFs != null)
            AddBlueprintsFromFileSystem(diskFs, fromDisk: true, recipes, seen);

        // 2. Load non-encrypted blueprints stored in the lathe computer's limited HDD memory.
        var hasHdd = _fileSystem.TryFindFileSystem(compUid, out _, out var hddFs);
        if (hasHdd && hddFs != null)
            AddBlueprintsFromFileSystem(hddFs, fromDisk: false, recipes, seen);

        // Show inserted disk designs first, then in-memory designs alphabetically.
        recipes.Sort((a, b) =>
        {
            var diskCmp = b.FromDisk.CompareTo(a.FromDisk);
            return diskCmp != 0
                ? diskCmp
                : string.Compare(a.RecipeName, b.RecipeName, StringComparison.OrdinalIgnoreCase);
        });

        var state = new LatheControlUiState(
            comp.HasTelemetry,
            dlEnabled,
            comp.LatheStatus,
            comp.CurrentPrinting,
            new List<string>(comp.QueueRecipes),
            new Dictionary<string, int>(comp.Materials),
            recipes,
            hasDisk ? liveDiskName : null,
            comp.StatusMessage,
            comp.ProductionStartTime,
            comp.ProductionLength,
            comp.MaterialUseMultiplier,
            new List<LatheQueueEntryDto>(comp.QueueEntries),
            hddFs?.UsedStorageBytes ?? 0,
            hddFs?.MaxStorageBytes ?? 0,
            comp.SelectedRecipeId,
            comp.SelectedRecipeFilePath,
            comp.SelectedRecipeFromDisk);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
