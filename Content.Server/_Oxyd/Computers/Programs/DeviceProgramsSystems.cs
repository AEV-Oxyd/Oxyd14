using System.Linq;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
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
            subs.Event<ApcControlActionMessage>(OnApcUiAction);
        });

        // Atmos / AirAlarm Control
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramStartupEvent>(OnAtmosStartup);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramActivatedEvent>(OnAtmosActivated);
        SubscribeLocalEvent<AtmosControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnAtmosDeviceLink);
        Subs.BuiEvents<AtmosControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<AtmosControlActionMessage>(OnAtmosUiAction);
        });

        // Lathe Control
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramStartupEvent>(OnLatheStartup);
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramActivatedEvent>(OnLatheActivated);
        SubscribeLocalEvent<LatheControlProgramComponent, ProgramDeviceLinkReceivedEvent>(OnLatheDeviceLink);
        Subs.BuiEvents<LatheControlProgramComponent>(ProgramUiKey.Key, subs =>
        {
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

    private void OnApcDeviceLink(Entity<ApcControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "ApcStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.AreaName = pkt.GetString("AreaName", ent.Comp.AreaName) ?? ent.Comp.AreaName;
        ent.Comp.MainBreakerEnabled = pkt.GetString("MainBreaker", "False") == "True";
        ent.Comp.BreakerTripped = pkt.GetString("BreakerTripped", "False") == "True";
        ent.Comp.Locked = pkt.GetString("Locked", "False") == "True";
        if (float.TryParse(pkt.GetString("ChargePercent", "0"), out var pct))
            ent.Comp.ChargePercent = pct;
        ent.Comp.LoadWatts = pkt.GetInt("LoadWatts", 0);
        ent.Comp.ExternalPowerState = pkt.GetString("ExternalPower", "None") ?? "None";
        ent.Comp.StatusMessage = "DeviceLink telemetry synchronized.";
        Dirty(ent);

        UpdateApcUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnApcUiAction(Entity<ApcControlProgramComponent> ent, ref ApcControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

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

    private void OnAtmosDeviceLink(Entity<AtmosControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "AirAlarmStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.AlarmLevel = pkt.GetString("AlarmLevel", ent.Comp.AlarmLevel) ?? ent.Comp.AlarmLevel;
        ent.Comp.CurrentMode = pkt.GetString("Mode", ent.Comp.CurrentMode) ?? ent.Comp.CurrentMode;
        ent.Comp.AutoMode = pkt.GetString("AutoMode", "True") == "True";
        if (float.TryParse(pkt.GetString("Pressure", "0"), out var pres))
            ent.Comp.PressureKpa = pres;
        if (float.TryParse(pkt.GetString("TemperatureC", "20"), out var temp))
            ent.Comp.TemperatureCelsius = temp;
        ent.Comp.VentCount = pkt.GetInt("Vents", 0);
        ent.Comp.ScrubberCount = pkt.GetInt("Scrubbers", 0);
        ent.Comp.SensorCount = pkt.GetInt("Sensors", 0);
        ent.Comp.GasSummary = pkt.GetString("GasSummary", ent.Comp.GasSummary) ?? ent.Comp.GasSummary;
        ent.Comp.StatusMessage = "Air Alarm DeviceLink synchronized.";
        Dirty(ent);

        UpdateAtmosUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    private void OnAtmosUiAction(Entity<AtmosControlProgramComponent> ent, ref AtmosControlActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

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

    private void OnLatheDeviceLink(Entity<LatheControlProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        if (args.Packet is not { PacketType: "AutolatheStatus" } pkt)
            return;

        ent.Comp.HasTelemetry = true;
        ent.Comp.LatheStatus = pkt.GetString("Status", "Standby") ?? "Standby";
        ent.Comp.CurrentPrinting = pkt.GetString("Recipe", string.Empty) ?? string.Empty;

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

        var recRaw = pkt.GetString("HostRecipesRaw", string.Empty);
        ent.Comp.HostRecipes = string.IsNullOrEmpty(recRaw)
            ? new List<string>()
            : recRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var diskName = pkt.GetString("DiskName", string.Empty);
        ent.Comp.InsertedDiskName = string.IsNullOrEmpty(diskName) ? null : diskName;
        ent.Comp.StatusMessage = "Autolathe DeviceLink synchronized.";
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

            case LatheControlAction.DecryptBlueprint:
                if (_fileSystem.TryFindFileSystem(compUid, out var decDrive, out var decFs) &&
                    _fileSystem.TryGetFile<RecipeBlueprintFile>(decDrive.Value, args.FilePath, out var encBp, decFs))
                {
                    encBp.IsEncrypted = false;
                    _fileSystem.TryWriteFile(decDrive.Value, encBp, decFs);
                    ent.Comp.StatusMessage = $"Decrypted blueprint '{args.FilePath}' on local hard drive.";
                }
                break;

            case LatheControlAction.ImportDiskBlueprints:
                var importPkt = new ComputerPacket("sync_disk_to_host");
                _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheBlueprintSink", isHostPort: true, "import_disk", args.FilePath, out _, args.Actor, importPkt);
                break;

            case LatheControlAction.PrintRecipe:
                var pkt = new ComputerPacket("lathe_print");
                pkt.Set("Recipe", args.RecipeId);
                pkt.Set("Amount", Math.Max(1, args.Amount));

                if (!string.IsNullOrEmpty(args.FilePath) &&
                    _fileSystem.TryFindFileSystem(compUid, out var bpDrive, out var bpFs) &&
                    _fileSystem.TryGetFile<RecipeBlueprintFile>(bpDrive.Value, args.FilePath, out var bpFile, bpFs))
                {
                    if (bpFile.IsEncrypted)
                    {
                        ent.Comp.StatusMessage = $"Blueprint '{args.FilePath}' is encrypted! Decrypt it first.";
                        break;
                    }

                    if (bpFile.DrmLocked && bpFile.RemainingUses is <= 0)
                    {
                        ent.Comp.StatusMessage = $"Blueprint '{args.FilePath}' has 0 DRM uses remaining.";
                        break;
                    }

                    pkt.SetReference("DriveUid", bpDrive.Value);
                    pkt.Set("FilePath", args.FilePath);
                    _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheBlueprintSink", isHostPort: true, "blueprint_print", args.RecipeId, out _, args.Actor, pkt);
                }
                else
                {
                    _computer.TryInvokeDeviceLinkPort(compUid, "AutolatheCommandSink", isHostPort: true, "lathe_print", args.RecipeId, out _, args.Actor, pkt);
                }
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

    private void UpdateLatheUi(EntityUid progUid, EntityUid compUid, LatheControlProgramComponent comp)
    {
        var dlEnabled = _computerQuery.TryComp(compUid, out var cComp) && cComp.AllowDeviceLink && !cComp.DeviceLinkWireCut;
        var recipes = new List<LatheBlueprintEntryDto>();
        var seenIds = new HashSet<string>();

        // 1. Read .lathe blueprints saved on local Hard Drive!
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            foreach (var (path, file) in fs.Files)
            {
                if (file is not RecipeBlueprintFile bp)
                    continue;

                foreach (var recId in bp.GetAllRecipes())
                {
                    seenIds.Add(recId);
                    var recName = _proto.TryIndex<LatheRecipePrototype>(recId, out var rProto) && rProto.Name != null
                        ? Loc.GetString(rProto.Name)
                        : recId.Id;
                    recipes.Add(new LatheBlueprintEntryDto(recId, recName, path, bp.RemainingUses, bp.DrmLocked, bp.IsEncrypted, fromDisk: false));
                }
            }
        }

        // 2. Host static/disk recipes reported via AutolatheStatus DeviceLink packet
        foreach (var recId in comp.HostRecipes)
        {
            if (seenIds.Contains(recId))
                continue;

            var recName = _proto.TryIndex<LatheRecipePrototype>(recId, out var rProto) && rProto.Name != null
                ? Loc.GetString(rProto.Name)
                : recId;
            recipes.Add(new LatheBlueprintEntryDto(recId, recName, string.Empty, null, false, false, fromDisk: false));
        }

        var state = new LatheControlUiState(
            comp.HasTelemetry,
            dlEnabled,
            comp.LatheStatus,
            comp.CurrentPrinting,
            new List<string>(comp.QueueRecipes),
            new Dictionary<string, int>(comp.Materials),
            recipes,
            comp.InsertedDiskName,
            comp.StatusMessage);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
