using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Pure DeviceLink + Hard-Drive file systems for APC Control, Air Alarm Control, and Autolathe Control.
/// Zero direct machine component reads/writes; everything flows through DeviceLink packets and HDD files.
/// </summary>
public sealed partial class DeviceProgramsSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<OxydComputerComponent> _computerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();
        _computerQuery = GetEntityQuery<OxydComputerComponent>();

        // APC Control
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramStartupEvent>(OnApcStartup);
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramActivatedEvent>(OnApcActivated);
        SubscribeLocalEvent<ApcControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnApcDeviceLink);
        Subs.BuiEvents<ApcControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnApcBuiOpened);
            subs.Event<ApcControlActionMessage>(OnApcUiAction);
        });

        // Atmos / AirAlarm Control
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramStartupEvent>(OnAtmosStartup);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramActivatedEvent>(OnAtmosActivated);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnAtmosDeviceLink);
        Subs.BuiEvents<AtmosControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnAtmosBuiOpened);
            subs.Event<AtmosControlActionMessage>(OnAtmosUiAction);
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

    private void OnApcUiAction(Entity<ApcControlProgramComponent> ent, ref ApcControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (ent.Comp.Locked)
        {
            ent.Comp.StatusMessage = "Action blocked: APC is LOCKED. Swipe authorized ID card on APC to unlock.";
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
        var sent = _computer.TryInvokeDeviceLinkPort(compUid, "ApcCommandSink", isHostPort: true, cmd, arg, out var err, actor, pkt);
        if (!sent)
        {
            // Fallback to MachineCommandReceiver if host only defines generic sink
            sent = _computer.TryInvokeDeviceLinkPort(compUid, "MachineCommandReceiver", isHostPort: true, cmd, arg, out err, actor, pkt);
        }

        if (!sent)
            comp.StatusMessage = $"DeviceLink error: {err}";
    }

    private void UpdateApcUi(EntityUid progUid, EntityUid compUid, ApcControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
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
            comp.StatusMessage);

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
        if (float.TryParse(pkt.GetString("Pressure", "0"), out var pres))
            ent.Comp.PressureKpa = pres;
        if (float.TryParse(pkt.GetString("TemperatureC", "20"), out var temp))
            ent.Comp.TemperatureCelsius = temp;
        ent.Comp.VentCount = pkt.GetInt("Vents", 0);
        ent.Comp.ScrubberCount = pkt.GetInt("Scrubbers", 0);
        ent.Comp.SensorCount = pkt.GetInt("Sensors", 0);
        ent.Comp.GasSummary = pkt.GetString("GasSummary", ent.Comp.GasSummary) ?? ent.Comp.GasSummary;
        ent.Comp.StatusMessage = string.Empty;
        Dirty(ent);

        UpdateAtmosUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnAtmosUiAction(Entity<AtmosControlProgramComponent> ent, ref AtmosControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (ent.Comp.Locked)
        {
            ent.Comp.StatusMessage = "Action blocked: Air Alarm is LOCKED. Swipe authorized ID card on Air Alarm to unlock.";
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

    private void SendAtmosDeviceLink(EntityUid compUid, AtmosControlProgramComponent comp, string cmd, string arg, EntityUid? actor)
    {
        var pkt = new ComputerPacket(cmd);
        pkt.Set("Argument", arg);
        var sent = _computer.TryInvokeDeviceLinkPort(compUid, "AirAlarmCommandSink", isHostPort: true, cmd, arg, out var err, actor, pkt);
        if (!sent)
            sent = _computer.TryInvokeDeviceLinkPort(compUid, "MachineCommandReceiver", isHostPort: true, cmd, arg, out err, actor, pkt);

        if (!sent)
            comp.StatusMessage = $"DeviceLink error: {err}";
    }

    private void UpdateAtmosUi(EntityUid progUid, EntityUid compUid, AtmosControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
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
            comp.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
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
