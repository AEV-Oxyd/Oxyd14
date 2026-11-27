using Content.Server.Instruments;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access.Components;
using Content.Shared.PDA;
using Content.Shared.PDA.Ringer;
using Robust.Shared.Maths;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Server system for the dedicated handheld PDA Operating System (<see cref="PdaOsProgramComponent"/>).
/// Bridges PDA hardware slots, station status, pocket utilities, and child program lifecycles.
/// </summary>
public sealed partial class PdaOsSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private OsProgramSystem _osPrograms = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<ComputerStatsComponent> _statsQuery = default!;
    [Dependency] private EntityQuery<PdaComponent> _pdaQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<PdaOsProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnPdaOsBuiOpened);
            subs.Event<PdaOsSetViewMessage>(OnPdaOsSetView);
            subs.Event<PdaOsLaunchProgramMessage>(OnPdaOsLaunchProgram);
            subs.Event<PdaOsSwitchProgramMessage>(OnPdaOsSwitchProgram);
            subs.Event<PdaOsCloseProgramMessage>(OnPdaOsCloseProgram);
            subs.Event<PdaOsHardwareActionMessage>(OnPdaOsHardwareAction);
            subs.Event<PdaOsInvokeDevicePortMessage>(OnPdaOsInvokePort);
            subs.Event<PdaOsShutDownMessage>(OnPdaOsShutDown);
        });
    }

    [SubscribeLocalEvent]
    private void OnComputerHardwareChanged(Entity<PdaOsProgramComponent> ent, ref ComputerHardwareChangedEvent args)
    {
        UpdatePdaOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPdaOsStartup(Entity<PdaOsProgramComponent> ent, ref ProgramStartupEvent args)
    {
        _computer.SendPacketToHost(args.ComputerUid, "MachineCommandReceiver", new ComputerPacket("status_request"));
        UpdatePdaOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPdaOsActivated(Entity<PdaOsProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdatePdaOsUi(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnPdaOsDeviceLinkReceived(Entity<PdaOsProgramComponent> ent, ref ProgramDeviceLinkReceivedEvent args)
    {
        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsBuiOpened(Entity<PdaOsProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsSetView(Entity<PdaOsProgramComponent> ent, ref PdaOsSetViewMessage args)
    {
        ent.Comp.CurrentView = Math.Clamp(args.ViewIndex, 0, 2);
        Dirty(ent);
        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsLaunchProgram(Entity<PdaOsProgramComponent> ent, ref PdaOsLaunchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (_computer.TryLaunchExecutable(compUid, args.ExecutablePath, out _, out _, args.Actor))
        {
            ent.Comp.CurrentView = 2;
            Dirty(ent);
        }

        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsSwitchProgram(Entity<PdaOsProgramComponent> ent, ref PdaOsSwitchProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        var target = GetEntity(args.ProgramEntity);
        if (_computer.SwitchActiveProgram(compUid, target, actor: args.Actor))
        {
            ent.Comp.CurrentView = 2;
            Dirty(ent);
        }

        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsCloseProgram(Entity<PdaOsProgramComponent> ent, ref PdaOsCloseProgramMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        var target = GetEntity(args.ProgramEntity);
        _computer.CloseProgram(compUid, target);

        if (_computerQuery.TryComp(compUid, out var comp) &&
            (comp.ActiveProgram == null || comp.ActiveProgram == ent.Owner) &&
            ent.Comp.CurrentView == 2)
        {
            ent.Comp.CurrentView = 0;
            Dirty(ent);
        }

        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsHardwareAction(Entity<PdaOsProgramComponent> ent, ref PdaOsHardwareActionMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        var pkt = new ComputerPacket(args.Action);
        pkt.Set("Argument", args.Argument);
        _computer.SendPacketToHost(compUid, "MachineCommandReceiver", pkt, args.Actor);
        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsInvokePort(Entity<PdaOsProgramComponent> ent, ref PdaOsInvokeDevicePortMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        _computer.TryInvokeDeviceLinkPort(compUid, args.PortId, args.IsHostPort, args.Command, args.Argument, out _, args.Actor);
        UpdatePdaOsUi(ent.Owner);
    }

    private void OnPdaOsShutDown(Entity<PdaOsProgramComponent> ent, ref PdaOsShutDownMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        _computer.ShutDownComputer(compUid, manualShutdown: true);
    }

    public void UpdatePdaOsUi(EntityUid progUid)
    {
        if (!TryComp<PdaOsProgramComponent>(progUid, out var pdaOsComp))
            return;

        if (!_programQuery.TryComp(progUid, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computerQuery.TryComp(compUid, out var comp))
            return;

        var stats = _statsQuery.CompOrNull(compUid) ?? EnsureComp<ComputerStatsComponent>(compUid);
        var (installed, running) = _osPrograms.BuildProgramLists(compUid, comp);
        var ports = _computer.GetMirroredPorts(compUid, comp);
        var hwSummary = _osPrograms.BuildHardwareSummary(compUid, comp, stats);

        NetEntity? activeChild = comp.ActiveProgram is { } ap && Exists(ap) && ap != progUid
            ? GetNetEntity(ap)
            : null;

        string? ownerName = null;
        string? ownerJob = null;
        string? stationName = null;
        string? alertLevel = null;
        var alertColor = Color.White;
        string? idName = null;
        string? penName = null;
        string? paiName = null;
        string? diskName = null;
        var flashlightOn = false;
        var hasRinger = false;
        var hasInstrument = false;
        var hasUplinkUnlocked = false;

        if (comp.HostMachine is { } hm && Exists(hm))
        {
            hasRinger = HasComp<RingerComponent>(hm);
            hasInstrument = HasComp<InstrumentComponent>(hm);
            hasUplinkUnlocked = TryComp<RingerUplinkComponent>(hm, out var uplink) && uplink.Unlocked;

            if (_pdaQuery.TryComp(hm, out var pda))
            {
                ownerName = pda.OwnerName;
                stationName = pda.StationName;
                alertLevel = pda.StationAlertLevel?.Id;
                alertColor = pda.StationAlertColor;
                flashlightOn = pda.FlashlightOn;

                var idUid = pda.IdSlot.Item ?? pda.ContainedId;
                if (idUid is { Valid: true } id && Exists(id))
                {
                    idName = MetaData(id).EntityName;
                    if (_idCardQuery.TryComp(id, out var idCard))
                    {
                        if (!string.IsNullOrWhiteSpace(idCard.FullName))
                            ownerName = idCard.FullName;
                        ownerJob = idCard.LocalizedJobTitle;
                    }
                }

                if (pda.PenSlot.Item is { Valid: true } pen && Exists(pen))
                    penName = MetaData(pen).EntityName;

                if (pda.PaiSlot.Item is { Valid: true } pai && Exists(pai))
                    paiName = MetaData(pai).EntityName;

                var diskUid = pda.DiskSlot.Item ?? pda.ContainedDisk;
                if (diskUid is { Valid: true } disk && Exists(disk))
                    diskName = MetaData(disk).EntityName;
            }
        }

        var state = new PdaOsUiState(
            comp.HostTitle,
            comp.HostSubtitle,
            comp.BorderColor,
            ownerName,
            ownerJob,
            stationName,
            alertLevel,
            alertColor,
            idName,
            penName,
            paiName,
            diskName,
            flashlightOn,
            hasRinger,
            hasInstrument,
            hasUplinkUnlocked,
            pdaOsComp.CurrentView,
            activeChild,
            installed,
            running,
            ports,
            hwSummary);

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
            _ui.SetUiState(progUid, ProgramUiKey.Key, state);
    }
}
