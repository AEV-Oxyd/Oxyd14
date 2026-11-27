using Content.Server._Oxyd.Computers.Network;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access.Components;
using Content.Shared.Lathe;
using Content.Shared.Lock;
using Content.Shared.PDA;
using Content.Shared.PDA.Ringer;
using Content.Shared.Store.Components;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server logic for both desktop/PDA OS (<see cref="OsProgramComponent"/>) and
/// subsystem Embedded OS (<see cref="EmbeddedOsProgramComponent"/>).
/// Both are entity-based boot programs delivering direct BUI states via ProgramUiKey.Key.
/// </summary>
public sealed partial class OsProgramSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private RecordsProgramsSystem _recordsPrograms = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<PdaComponent> _pdaQuery = default!;
    [Dependency] private EntityQuery<ApcComponent> _apcQuery = default!;
    [Dependency] private EntityQuery<AirAlarmComponent> _airAlarmQuery = default!;
    [Dependency] private EntityQuery<LatheComponent> _latheQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<OsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOsBuiOpened);
            subs.Event<OsSetViewMessage>(OnOsSetView);
            subs.Event<OsLaunchProgramMessage>(OnOsLaunchProgram);
            subs.Event<OsSwitchProgramMessage>(OnOsSwitchProgram);
            subs.Event<OsCloseProgramMessage>(OnOsCloseProgram);
            subs.Event<OsHardwareActionMessage>(OnOsHardwareAction);
            subs.Event<OsInvokeDevicePortMessage>(OnOsInvokePort);
            subs.Event<OsShutDownMessage>(OnOsShutDown);
        });

        Subs.BuiEvents<EmbeddedOsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnEmbeddedBuiOpened);
            subs.Event<EmbeddedOsSetViewMessage>(OnEmbeddedSetView);
            subs.Event<EmbeddedOsLaunchProgramMessage>(OnEmbeddedLaunchProgram);
            subs.Event<EmbeddedOsSwitchProgramMessage>(OnEmbeddedSwitchProgram);
            subs.Event<EmbeddedOsCloseProgramMessage>(OnEmbeddedCloseProgram);
            subs.Event<EmbeddedOsInvokePortMessage>(OnEmbeddedInvokePort);
            subs.Event<EmbeddedOsShutDownMessage>(OnEmbeddedShutDown);
        });
    }

    [SubscribeLocalEvent]
    private void OnComputerHardwareChanged(Entity<OxydComputerComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        if (ent.Comp.BootProgram is { } boot && Exists(boot))
            UpdateOsUi(boot);
    }

    [SubscribeLocalEvent]
    private void OnOsStartup(Entity<OsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        _computer.SendPacketToHost(args.ComputerUid, "MachineCommandReceiver", new ComputerPacket("status_request"));
        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnOsActivated(Entity<OsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnOsDeviceLinkReceived(Entity<OsProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    private void OnOsBuiOpened(Entity<OsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnEmbeddedStartup(Entity<EmbeddedOsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        if (!ent.Comp.DefaultAppLaunched &&
            _computerQuery.TryComp(args.ComputerUid, out var comp) &&
            !string.IsNullOrWhiteSpace(comp.EmbeddedDefaultAppPath))
        {
            ent.Comp.DefaultAppLaunched = true;
            _computer.TryLaunchExecutable(args.ComputerUid, comp.EmbeddedDefaultAppPath, out _, out _);
        }

        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnEmbeddedActivated(Entity<EmbeddedOsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnEmbeddedDeviceLinkReceived(Entity<EmbeddedOsProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    private void OnOsSetView(Entity<OsProgramComponent> ent, ref OsSetViewMessage args)
    {
        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        // View 1 (legacy separate Apps tab) is merged into View 0 (Desktop & App Launcher).
        var view = Math.Clamp(args.ViewIndex, 0, 3);
        ent.Comp.CurrentView = view == 1 ? 0 : view;
        Dirty(ent);
        UpdateOsUi(ent.Owner);
    }

    private void OnOsLaunchProgram(Entity<OsProgramComponent> ent, ref OsLaunchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        if (_computer.TryLaunchExecutable(compUid, args.ExecutablePath, out _, out _, args.Actor))
        {
            ent.Comp.CurrentView = 3;
            Dirty(ent);
        }
        UpdateOsUi(ent.Owner);
    }

    private void OnOsSwitchProgram(Entity<OsProgramComponent> ent, ref OsSwitchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        if (TryGetEntity(args.ProgramEntity, out var target) &&
            _computer.SwitchActiveProgram(compUid, target.Value, actor: args.Actor))
        {
            ent.Comp.CurrentView = 3;
            Dirty(ent);
        }
        UpdateOsUi(ent.Owner);
    }

    private void OnOsCloseProgram(Entity<OsProgramComponent> ent, ref OsCloseProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        if (TryGetEntity(args.ProgramEntity, out var target))
            _computer.CloseProgram(compUid, target.Value);
        UpdateOsUi(ent.Owner);
    }

    private void OnOsHardwareAction(Entity<OsProgramComponent> ent, ref OsHardwareActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        var pkt = new ComputerPacket(args.Action);
        pkt.Set("Argument", args.Argument);
        _computer.SendPacketToHost(compUid, "MachineCommandReceiver", pkt, args.Actor);
        UpdateOsUi(ent.Owner);
    }

    private void OnOsInvokePort(Entity<OsProgramComponent> ent, ref OsInvokeDevicePortMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.TryInvokeDeviceLinkPort(compUid, args.PortId, args.IsHostPort, args.Command, args.Argument, out _, args.Actor);
        UpdateOsUi(ent.Owner);
    }

    private void OnOsShutDown(Entity<OsProgramComponent> ent, ref OsShutDownMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.ShutDownComputer(compUid, manualShutdown: true);
    }

    [SubscribeLocalEvent]
    private void OnHostLockToggled(Entity<EmbeddedComputerHostComponent> ent, ref LockToggledEvent args)
    {
        if (ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        if (comp.BootProgram is not { } bootUid || !Exists(bootUid))
            return;

        if (args.Locked && TryComp<EmbeddedOsProgramComponent>(bootUid, out var embComp))
        {
            if (embComp.CurrentView != 0)
            {
                embComp.CurrentView = 0;
                Dirty(bootUid, embComp);
            }

            var isTwoTabController =
                string.Equals(comp.EmbeddedDefaultAppPath, "/bin/apc_control.nte", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comp.EmbeddedDefaultAppPath, "/bin/lathe_control.nte", StringComparison.OrdinalIgnoreCase);
            if (isTwoTabController)
            {
                foreach (var rUid in comp.RunningPrograms)
                {
                    if (_programQuery.TryComp(rUid, out var rProg) &&
                        string.Equals(rProg.ExecutablePath, comp.EmbeddedDefaultAppPath, StringComparison.OrdinalIgnoreCase) &&
                        comp.ActiveProgram != rUid)
                    {
                        _computer.SwitchActiveProgram(compUid, rUid);
                        break;
                    }
                }
            }
        }

        UpdateOsUi(bootUid);
        _recordsPrograms.RefreshRecordsForHostLock(compUid, args.Locked);
    }

    private bool IsHostLocked(EntityUid compUid)
    {
        return _computerQuery.TryComp(compUid, out var comp) &&
               comp.HostMachine is { } hm &&
               _lock.IsLocked(hm);
    }

    private void OnEmbeddedBuiOpened(Entity<EmbeddedOsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedSetView(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsSetViewMessage args)
    {
        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _, checkHostLock: false))
            return;

        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid && IsHostLocked(compUid))
        {
            if (ent.Comp.CurrentView != 0)
            {
                ent.Comp.CurrentView = 0;
                Dirty(ent);
            }
            UpdateOsUi(ent.Owner);
            return;
        }

        ent.Comp.CurrentView = Math.Clamp(args.ViewIndex, 0, 3);
        Dirty(ent);
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedLaunchProgram(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsLaunchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
        {
            UpdateOsUi(ent.Owner);
            return;
        }

        if (_computer.TryLaunchExecutable(compUid, args.ExecutablePath, out _, out _, args.Actor))
        {
            ent.Comp.CurrentView = 0;
            Dirty(ent);
        }
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedSwitchProgram(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsSwitchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
        {
            UpdateOsUi(ent.Owner);
            return;
        }

        if (TryGetEntity(args.ProgramEntity, out var target) &&
            _computer.SwitchActiveProgram(compUid, target.Value, actor: args.Actor))
        {
            ent.Comp.CurrentView = 0;
            Dirty(ent);
        }
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedCloseProgram(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsCloseProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
        {
            UpdateOsUi(ent.Owner);
            return;
        }

        if (TryGetEntity(args.ProgramEntity, out var target))
            _computer.CloseProgram(compUid, target.Value);
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedInvokePort(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsInvokePortMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
        {
            UpdateOsUi(ent.Owner);
            return;
        }

        _computer.TryInvokeDeviceLinkPort(compUid, args.PortId, args.IsHostPort, args.Command, args.Argument, out _, args.Actor);
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedShutDown(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsShutDownMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
        {
            UpdateOsUi(ent.Owner);
            return;
        }

        _computer.ShutDownComputer(compUid, manualShutdown: true);
    }

    public void UpdateOsUi(EntityUid progUid)
    {
        if (!_programQuery.TryComp(progUid, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var stats = _statsQuery.CompOrNull(compUid) ?? EnsureComp<ComputerStatsComponent>(compUid);
        var (installed, running) = BuildProgramLists(compUid, comp);
        var ports = _computer.GetMirroredPorts(compUid, comp);
        var hwSummary = BuildHardwareSummary(compUid, comp, stats);

        NetEntity? activeChild = comp.ActiveProgram is { } ap && Exists(ap) && ap != progUid
            ? GetNetEntity(ap)
            : null;

        if (TryComp<OsProgramComponent>(progUid, out var osComp))
        {
            string? ownerName = null;
            string? ownerJob = null;
            string? diskName = null;
            var flashlightOn = false;

            if (comp.HostMachine is { } hm && _pdaQuery.TryComp(hm, out var pda))
            {
                ownerName = pda.OwnerName;
                if (pda.ContainedId is { } idUid && TryComp<IdCardComponent>(idUid, out var idCard))
                    ownerJob = idCard.LocalizedJobTitle;
                flashlightOn = pda.FlashlightOn;
                if (pda.ContainedDisk is { } dUid && Exists(dUid))
                    diskName = MetaData(dUid).EntityName;
            }

            var state = new OsUiState(
                comp.HostTitle,
                comp.HostSubtitle,
                comp.BorderColor,
                ownerName,
                ownerJob,
                diskName,
                flashlightOn,
                osComp.CurrentView,
                activeChild,
                installed,
                running,
                ports,
                hwSummary);

            if (_ui.HasUi(progUid, ProgramUiKey.Key))
                _ui.SetUiState(progUid, ProgramUiKey.Key, state);
        }
        else if (TryComp<EmbeddedOsProgramComponent>(progUid, out var embComp))
        {
            var hostName = comp.HostMachine is { } hm && Exists(hm)
                ? MetaData(hm).EntityName
                : MetaData(compUid).EntityName;

            var hasLock = comp.HostMachine is { } lockHost && HasComp<LockComponent>(lockHost);
            var isHostLocked = hasLock && _lock.IsLocked(comp.HostMachine!.Value);
            var isDedicatedPanel = comp.HostMachine is { } panelHost &&
                                   (_apcQuery.HasComp(panelHost) || _airAlarmQuery.HasComp(panelHost) || _latheQuery.HasComp(panelHost));

            var state = new EmbeddedOsUiState(
                comp.HostTitle,
                comp.HostSubtitle,
                hostName,
                _router.EnsureDeviceGuid(compUid),
                isHostLocked ? 0 : embComp.CurrentView,
                activeChild,
                installed,
                running,
                ports,
                hwSummary,
                hasLock,
                isHostLocked,
                comp.EmbeddedDefaultAppPath,
                isDedicatedPanel);

            if (_ui.HasUi(progUid, ProgramUiKey.Key))
                _ui.SetUiState(progUid, ProgramUiKey.Key, state);
        }
    }

    internal (List<OsProgramEntryDto> installed, List<OsProgramEntryDto> running) BuildProgramLists(EntityUid compUid, OxydComputerComponent comp)
    {
        var installed = new List<OsProgramEntryDto>();
        var running = new List<OsProgramEntryDto>();
        var runningByPath = new Dictionary<string, EntityUid>(StringComparer.OrdinalIgnoreCase);

        foreach (var rUid in comp.RunningPrograms)
        {
            if (!Exists(rUid) || !_programQuery.TryComp(rUid, out var rProg))
                continue;

            running.Add(new OsProgramEntryDto(
                GetNetEntity(rUid),
                rProg.ExecutablePath,
                rProg.Title,
                rProg.Prototype ?? string.Empty,
                rProg.Icon,
                ComputerMetricsHelper.GetProgramRamUsageMb(rProg),
                ComputerMetricsHelper.GetProgramCpuUsagePercent(rProg),
                true,
                rProg.IsBootProgram));

            if (!string.IsNullOrEmpty(rProg.ExecutablePath))
                runningByPath[rProg.ExecutablePath] = rUid;
        }

        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            foreach (var (path, file) in fs.Files)
            {
                if (file is not ProgramVirtualFile pvf || !pvf.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase))
                    continue;

                runningByPath.TryGetValue(path, out var activeEnt);
                installed.Add(new OsProgramEntryDto(
                    activeEnt.IsValid() ? GetNetEntity(activeEnt) : null,
                    path,
                    string.IsNullOrEmpty(pvf.Title) ? pvf.Name : pvf.Title,
                    pvf.ProgramId,
                    null,
                    pvf.RamUsageMb,
                    pvf.CpuUsagePercent,
                    activeEnt.IsValid(),
                    comp.BootProgram == activeEnt && activeEnt.IsValid()));
            }
        }

        return (installed, running);
    }

    internal OsHardwareSummaryDto BuildHardwareSummary(EntityUid compUid, OxydComputerComponent comp, ComputerStatsComponent stats)
    {
        var hostName = comp.HostMachine is { } hm && Exists(hm)
            ? MetaData(hm).EntityName
            : MetaData(compUid).EntityName;

        return new OsHardwareSummaryDto(
            stats.CpuFrequencyMhz,
            stats.CurrentCpuLoadPercent,
            stats.MaxConcurrentPrograms,
            stats.UsedRamMb,
            stats.RamMb,
            stats.UsedStorageBytes,
            stats.StorageBytes,
            stats.NetworkBandwidthBps,
            stats.NetworkUploadBandwidthBps,
            stats.HasNetworkCard,
            stats.HasDataPort,
            stats.HasDiskSlotModule || comp.Template == ComputerTemplate.Pda,
            stats.HasIdSlotModule || comp.Template == ComputerTemplate.Pda,
            comp.IsPowered,
            comp.AllowRemoteControl && !comp.RemoteControlWireCut,
            comp.AllowDeviceLink && !comp.DeviceLinkWireCut,
            _router.EnsureDeviceGuid(compUid),
            hostName,
            comp.IsBooted ? "SYSTEM ONLINE" : comp.BootError,
            _router.GetConnectedNetworkLabel(compUid));
    }
}
