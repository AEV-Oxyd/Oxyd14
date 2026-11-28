using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
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
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

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
            subs.Event<OsLogoutAuthMessage>(OnOsLogoutAuth);
            subs.Event<OsSwipeAuthMessage>(OnOsSwipeAuth);
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
            subs.Event<EmbeddedOsSwipeAuthMessage>(OnEmbeddedSwipeAuth);
            subs.Event<EmbeddedOsLogoutAuthMessage>(OnEmbeddedLogoutAuth);
        });
    }

    [SubscribeLocalEvent]
    private void OnComputerHardwareChanged(Entity<OxydComputerComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        if (ent.Comp.BootProgram is { } boot && Exists(boot))
            UpdateOsUi(boot);
    }

    [SubscribeLocalEvent]
    private void OnComputerOsAuthChanged(Entity<OxydComputerComponent> ent, ref ComputerOsAuthChangedEvent args)
    {
        if (ent.Comp.BootProgram is { } boot && Exists(boot))
            UpdateOsUi(boot);
    }

    [SubscribeLocalEvent]
    private void OnDriveFileSystemChanged(ref FileSystemChangedEvent args)
    {
        var toUpdate = new List<EntityUid>();
        var query = EntityQueryEnumerator<OxydComputerComponent, ComputerStatsComponent>();
        while (query.MoveNext(out var compUid, out var comp, out var stats))
        {
            if ((args.DriveUid.IsValid() && stats.PrimaryDriveUid != args.DriveUid) ||
                !comp.IsBooted ||
                comp.BootProgram is not { Valid: true } bootUid)
            {
                continue;
            }

            if (HasComp<OsProgramComponent>(bootUid) || HasComp<EmbeddedOsProgramComponent>(bootUid))
                toUpdate.Add(bootUid);
        }

        foreach (var bootUid in toUpdate)
        {
            if (Exists(bootUid))
                UpdateOsUi(bootUid);
        }
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
        var prevView = ent.Comp.CurrentView;
        var view = Math.Clamp(args.ViewIndex, 0, 3);
        var nextView = view == 1 ? 0 : view;
        ent.Comp.CurrentView = nextView;
        Dirty(ent);

        if (_programQuery.TryComp(ent.Owner, out var prog) &&
            prog.Computer is { } compUid &&
            _computerQuery.TryComp(compUid, out var comp) &&
            comp.ActiveProgram is { Valid: true } activeProg &&
            activeProg != ent.Owner &&
            Exists(activeProg))
        {
            if (prevView == 3 && nextView != 3)
            {
                var deactEv = new ProgramDeactivatedEvent(compUid, activeProg);
                RaiseLocalEvent(activeProg, ref deactEv);
            }
            else if (prevView != 3 && nextView == 3)
            {
                var actEv = new ProgramActivatedEvent(compUid, activeProg);
                RaiseLocalEvent(activeProg, ref actEv);
            }
        }

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

    private void OnOsLogoutAuth(Entity<OsProgramComponent> ent, ref OsLogoutAuthMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.ClearOsSession(compUid);
        UpdateOsUi(ent.Owner);
    }

    private void OnOsSwipeAuth(Entity<OsProgramComponent> ent, ref OsSwipeAuthMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.TryAuthenticateOsSessionFromActor(compUid, args.Actor, out _);
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedSwipeAuth(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsSwipeAuthMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.TryAuthenticateOsSessionFromActor(compUid, args.Actor, out _);
        UpdateOsUi(ent.Owner);
    }

    private void OnEmbeddedLogoutAuth(Entity<EmbeddedOsProgramComponent> ent, ref EmbeddedOsLogoutAuthMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computer.CanExecuteProgramAction(ent.Owner, args.Actor, out _))
            return;

        _computer.ClearOsSession(compUid);
        UpdateOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnHostLockToggled(Entity<EmbeddedComputerHostComponent> ent, ref LockToggledEvent args)
    {
        if (ent.Comp.ComputerEntity is not { } compUid || !_computerQuery.TryComp(compUid, out var comp))
            return;

        if (args.Locked)
        {
            _computer.ClearOsSession(compUid, comp);
            foreach (var progUid in comp.RunningPrograms.ToList())
            {
                if (progUid == comp.BootProgram || !Exists(progUid))
                    continue;

                var deactEv = new ProgramDeactivatedEvent(compUid, progUid);
                RaiseLocalEvent(progUid, ref deactEv);
            }
        }

        _computer.UpdateComputerDisplayUi(compUid, comp);

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

        var prevView = ent.Comp.CurrentView;
        var nextView = Math.Clamp(args.ViewIndex, 0, 3);
        ent.Comp.CurrentView = nextView;
        Dirty(ent);

        if (_programQuery.TryComp(ent.Owner, out var embProg) &&
            embProg.Computer is { } embCompUid &&
            _computerQuery.TryComp(embCompUid, out var embComp) &&
            embComp.ActiveProgram is { Valid: true } activeProg &&
            activeProg != ent.Owner &&
            Exists(activeProg))
        {
            if (prevView == 0 && nextView != 0)
            {
                var deactEv = new ProgramDeactivatedEvent(embCompUid, activeProg);
                RaiseLocalEvent(activeProg, ref deactEv);
            }
            else if (prevView != 0 && nextView == 0)
            {
                var actEv = new ProgramActivatedEvent(embCompUid, activeProg);
                RaiseLocalEvent(activeProg, ref actEv);
            }
        }

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
                hwSummary,
                string.IsNullOrWhiteSpace(comp.OsAuthLabel) ? null : comp.OsAuthLabel);

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
                isDedicatedPanel,
                string.IsNullOrWhiteSpace(comp.OsAuthLabel) ? null : comp.OsAuthLabel);

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
                var isBoot = comp.BootProgram == activeEnt && activeEnt.IsValid();

                // Never offer other OS kernels as launchable child apps inside a running OS.
                if (!isBoot && _computer.IsOperatingSystemExecutable(pvf.ProgramId))
                    continue;

                installed.Add(new OsProgramEntryDto(
                    activeEnt.IsValid() ? GetNetEntity(activeEnt) : null,
                    path,
                    string.IsNullOrEmpty(pvf.Title) ? pvf.Name : pvf.Title,
                    pvf.ProgramId,
                    null,
                    pvf.RamUsageMb,
                    pvf.CpuUsagePercent,
                    activeEnt.IsValid(),
                    isBoot));
            }
        }

        return (installed, running);
    }

    internal OsHardwareSummaryDto BuildHardwareSummary(EntityUid compUid, OxydComputerComponent comp, ComputerStatsComponent stats)
    {
        var hostName = comp.HostMachine is { } hm && Exists(hm)
            ? MetaData(hm).EntityName
            : MetaData(compUid).EntityName;

        var networkLabel = _router.GetConnectedNetworkLabel(compUid);
        var installedComponents = BuildInstalledComponentsList(compUid, comp, networkLabel);

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
            _computer.IsRemoteControlAllowed(compUid, comp),
            comp.AllowDeviceLink && !comp.DeviceLinkWireCut,
            _router.EnsureDeviceGuid(compUid),
            hostName,
            comp.IsBooted ? "SYSTEM ONLINE" : comp.BootError,
            networkLabel,
            installedComponents,
            stats.HasBattery,
            stats.BatteryCharge,
            stats.BatteryMaxCharge,
            _computer.TryGetInsertedIdCard(compUid, out _));
    }

    private List<OsHardwareComponentDto> BuildInstalledComponentsList(
        EntityUid compUid,
        OxydComputerComponent comp,
        string? networkLabel)
    {
        var list = new List<OsHardwareComponentDto>();
        var visited = new HashSet<EntityUid>();

        var orderedSlots = new (string SlotId, string SlotLabel)[]
        {
            (ModularComputerComponent.SlotCpu, "CPU Slot"),
            (ModularComputerComponent.SlotRam, "RAM Slot 1"),
            (ModularComputerComponent.SlotRamSecondary, "RAM Slot 2"),
            (ModularComputerComponent.SlotStorage, "Storage Bay"),
            (ModularComputerComponent.SlotNetwork, "Network Bay"),
            (ModularComputerComponent.SlotBattery, "Battery Bay"),
            (ModularComputerComponent.SlotSensor, "Sensor Bay"),
            (ModularComputerComponent.SlotIdModule, "ID Module 1"),
            (ModularComputerComponent.SlotIdModuleSecondary, "ID Module 2"),
            (ModularComputerComponent.SlotDiskModule, "Disk Module"),
            (ModularComputerComponent.SlotPrinter, "Printer Bay")
        };

        foreach (var (slotId, slotLabel) in orderedSlots)
        {
            if (_itemSlots.TryGetSlot(compUid, slotId, out var slot) &&
                slot.Item is { Valid: true } partUid &&
                Exists(partUid) &&
                !TerminatingOrDeleted(partUid) &&
                visited.Add(partUid))
            {
                list.Add(DescribeHardwareComponent(partUid, slotLabel, comp, networkLabel));
            }
        }

        foreach (var partUid in _computer.GetInstalledHardwareEntities(compUid))
        {
            if (visited.Add(partUid))
                list.Add(DescribeHardwareComponent(partUid, "Expansion", comp, networkLabel));
        }

        if (TryComp<ComputerDataPortComponent>(compUid, out var dataPort))
        {
            list.Add(new OsHardwareComponentDto(
                "Motherboard",
                "PORT",
                $"Diagnostic Data Port ({dataPort.PortLabel})",
                dataPort.RequireMaintenancePanelOpen ? "Local JTAG interface (panel access required)" : "Local JTAG diagnostic interface",
                dataPort.Enabled ? "ENABLED" : "DISABLED",
                dataPort.Enabled));
        }

        return list;
    }

    private OsHardwareComponentDto DescribeHardwareComponent(
        EntityUid partUid,
        string slotLabel,
        OxydComputerComponent comp,
        string? networkLabel)
    {
        var partName = MetaData(partUid).EntityName;

        if (TryComp<ComputerCpuComponent>(partUid, out var cpu))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "CPU",
                partName,
                $"{cpu.FrequencyMhz} MHz  •  {ComputerMetricsHelper.GetCpuCapacity(cpu):0} CPU capacity",
                "OK",
                true);
        }

        if (TryComp<ComputerRamComponent>(partUid, out var ram))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "RAM",
                partName,
                $"{ComputerSizeFormat.FormatRamQb(ram.SizeMb)} system memory",
                "OK",
                true);
        }

        if (TryComp<ComputerHardDriveComponent>(partUid, out var hdd))
        {
            var details = TryComp<FileSystemComponent>(partUid, out var fs)
                ? $"{ComputerSizeFormat.FormatStorageQb(fs.UsedStorageBytes)} / {ComputerSizeFormat.FormatStorageQb(fs.MaxStorageBytes)} used ({fs.Files.Count} files)"
                : $"{ComputerSizeFormat.FormatStorageQb(hdd.CapacityBytes)} capacity";
            return new OsHardwareComponentDto(
                slotLabel,
                "HDD",
                partName,
                details,
                comp.StorageWireCut ? "WIRE CUT" : "OK",
                !comp.StorageWireCut);
        }

        if (TryComp<ComputerNetworkCardComponent>(partUid, out var nic))
        {
            var mode = nic.SupportsWired && nic.SupportsWireless
                ? "Wired / Wireless"
                : nic.SupportsWireless ? "Wireless" : "Wired";
            var netSuffix = !string.IsNullOrWhiteSpace(networkLabel) ? $"  •  {networkLabel}" : string.Empty;
            var details = $"Down {ComputerSizeFormat.FormatRateQb(nic.BandwidthBps)}  •  Up {ComputerSizeFormat.FormatRateQb(nic.MaxUploadBps)}  •  {mode}{netSuffix}";
            var status = comp.NetworkWireCut ? "WIRE CUT" : !string.IsNullOrWhiteSpace(networkLabel) ? "ONLINE" : "READY";
            return new OsHardwareComponentDto(
                slotLabel,
                "NIC",
                partName,
                details,
                status,
                !comp.NetworkWireCut);
        }

        if (TryComp<ComputerBatteryModuleComponent>(partUid, out var bat))
        {
            var pct = bat.MaxCharge > 0f ? Math.Clamp(bat.CurrentCharge / bat.MaxCharge * 100f, 0f, 100f) : 0f;
            return new OsHardwareComponentDto(
                slotLabel,
                "BAT",
                partName,
                $"{bat.CurrentCharge:0}/{bat.MaxCharge:0} charge ({pct:0}%)",
                bat.CurrentCharge > 0f ? "OK" : "EMPTY",
                bat.CurrentCharge > 0f);
        }

        if (TryComp<ComputerIdCardSlotModuleComponent>(partUid, out var reader))
        {
            string details;
            string status;
            if (reader.Mode == ComputerCardReaderMode.Slot)
            {
                if (_itemSlots.TryGetSlot(partUid, reader.CardSlotId, out var cardSlot) &&
                    cardSlot.Item is { Valid: true } cardUid &&
                    Exists(cardUid))
                {
                    details = $"Retaining ID Slot  •  Inserted: {MetaData(cardUid).EntityName}";
                    status = "CARD IN";
                }
                else
                {
                    details = "Retaining ID Slot  •  Empty";
                    status = "READY";
                }
            }
            else
            {
                details = "Transient Swipe Scanner";
                status = "READY";
            }

            return new OsHardwareComponentDto(slotLabel, "ID", partName, details, status, true);
        }

        if (HasComp<ComputerDiskSlotModuleComponent>(partUid))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "DISK",
                partName,
                "Removable data disk drive bay",
                "READY",
                true);
        }

        if (HasComp<ComputerPrinterModuleComponent>(partUid))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "PRN",
                partName,
                "Thermal paper printer module",
                "READY",
                true);
        }

        if (HasComp<ComputerHealthScannerModuleComponent>(partUid))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "MED",
                partName,
                "Biometric health scanner sensor array",
                "OK",
                true);
        }

        if (HasComp<ComputerSensorModuleComponent>(partUid))
        {
            return new OsHardwareComponentDto(
                slotLabel,
                "SENS",
                partName,
                "Hardware telemetry & sensor interface",
                "OK",
                true);
        }

        return new OsHardwareComponentDto(slotLabel, "HW", partName, "Installed hardware module", "OK", true);
    }
}
