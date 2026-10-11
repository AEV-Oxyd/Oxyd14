using Content.Server.DoAfter;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Atmos;
using Content.Shared.Temperature.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Events;
using Content.Shared.Damage;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris handheld health scanner readout (tools/medical scanners): an organ table with
/// wounds, plus vitals, pain, NSA load and blood volume. The window is hosted on the patient.
/// </summary>
public sealed partial class OxydMedicalScannerSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydScannerItemComponent, AfterInteractEvent>(OnScannerInteract);
    }

    private void OnScannerInteract(Entity<OxydScannerItemComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } patient || !HasComp<BodyComponent>(patient))
            return;

        args.Handled = true;
        OpenScanUi(args.User, patient);
    }

    /// <summary>The BUI lives on the patient; each scan diagnoses every organ, unlocking
    /// wound details in the surgery UI (Eris diagnosed flag).</summary>
    public void OpenScanUi(EntityUid user, EntityUid patient)
    {
        _wounds.DiagnoseAll(patient);
        EnsureComp<UserInterfaceComponent>(patient);
        if (!_ui.HasUi(patient, OxydScannerUiKey.Key))
        {
            _ui.SetUi(patient, OxydScannerUiKey.Key,
                new InterfaceData("OxydScannerBoundUserInterface", 0f, false));
        }
        _ui.SetUiState(patient, OxydScannerUiKey.Key, BuildState(patient));
        _ui.OpenUi(patient, OxydScannerUiKey.Key, user);
    }

    private OxydScannerState BuildState(EntityUid patient)
    {
        var state = new OxydScannerState
        {
            HasScan = true,
            PatientName = Name(patient),
            Alive = _mobs.IsAlive(patient),
            Critical = _mobs.IsCritical(patient),
            Temperature = TryComp<TemperatureComponent>(patient, out var temp)
                ? temp.Temperature
                : Atmospherics.T20C,
        };

        if (TryComp<DamageableComponent>(patient, out var dmg))
        {
            var spec = _damage.GetPositiveDamage((patient, dmg));
            state.Health = spec.GetTotal().Float();
            state.BruteLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Brute);
            state.BurnLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Burn);
            state.ToxinLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Toxin);
            state.OxyLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Airloss);
        }

        if (TryComp<PainComponent>(patient, out var pain))
            state.Pain = pain.CurrentPain;

        if (TryComp<OxydNsaComponent>(patient, out var nsa))
            state.Nsa = nsa.Current;

        state.Pulse = _wounds.ClassifyPulse(patient, state.Critical, !state.Alive);

        if (_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood))
        {
            state.BloodLevel = blood.Volume.Float();
            // GetBloodLevel is 0..MaxVolumeModifier vs the real bloodstream max; the
            // solution's own MaxVolume is just container capacity (reads ~50% full).
            state.BloodMax = blood.Volume.Float() / MathF.Max(_bloodstream.GetBloodLevel(patient), 0.0001f);
        }

        foreach (var (orgUid, organ, surg) in _wounds.GetOrgans(patient))
        {
            var status = new List<string>();
            if (surg.Fractured)
                status.Add(Loc.GetString("oxyd-medical-scan-fractured"));
            if (surg is { Incision: not OxydIncisionStage.None, Clamped: false })
                status.Add(Loc.GetString("oxyd-medical-scan-bleeding"));
            if (surg.Incision != OxydIncisionStage.None)
                status.Add(Loc.GetString("oxyd-medical-scan-incision"));
            if (surg.Robotic)
                status.Add(Loc.GetString("oxyd-medical-scan-robotic"));
            if (surg.EmbeddedItems.Count > 0)
                status.Add(Loc.GetString("oxyd-medical-scan-embedded", ("count", surg.EmbeddedItems.Count)));

            state.Organs.Add(new OxydScannerOrgan
            {
                Name = Name(orgUid),
                Status = status.Count == 0
                    ? Loc.GetString("oxyd-medical-scan-normal")
                    : string.Join(", ", status),
                Damage = surg.OrganDamage,
                Fractured = surg.Fractured,
                Bleeding = surg is { Incision: not OxydIncisionStage.None, Clamped: false },
            });
        }

        return state;
    }
}

/// <summary>
/// Ports the Eris IV drip (machinery/iv_drip.dm): attach to a patient to transfer blood,
/// detach to stop. Ticks transfer a few units between the attached beaker and the bloodstream.
/// </summary>
public sealed partial class OxydIvDripSystem : EntitySystem
{
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Max distance the drip can be from the patient before the line detaches.</summary>
    private const float IvRange = 1.5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydIvDripComponent, ComponentInit>(OnInit);
        // Attach/detach by dragging either the drip onto the patient or the patient onto the drip.
        SubscribeLocalEvent<BodyComponent, DragDropTargetEvent>(OnDragOntoPatient);
        SubscribeLocalEvent<BodyComponent, CanDropTargetEvent>(OnCanDropOntoPatient);
        SubscribeLocalEvent<OxydIvDripComponent, DragDropTargetEvent>(OnDropPatientOnDrip);
        SubscribeLocalEvent<OxydIvDripComponent, CanDropTargetEvent>(OnCanDropPatientOnDrip);
        SubscribeLocalEvent<OxydIvDripComponent, GetVerbsEvent<AlternativeVerb>>(AddIvVerbs);
        SubscribeLocalEvent<OxydIvDripComponent, GetVerbsEvent<InteractionVerb>>(AddInsertBeakerVerb);
        SubscribeLocalEvent<OxydIvDripComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OxydIvDripComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<OxydIvDripComponent, OxydIvAttachDoAfterEvent>(OnAttachDoAfter);
    }

    /// <summary>Eris examine: reports the loaded tank and attached vessel + the mode.</summary>
    private void OnExamine(EntityUid uid, OxydIvDripComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        if (BeakerSlot(uid).ContainedEntity is { } beaker)
            args.PushMarkup(Loc.GetString("oxyd-medical-iv-examine-tank", ("tank", Name(beaker))));
        else
            args.PushMarkup(Loc.GetString("oxyd-medical-iv-examine-no-tank"));
        if (comp.AttachedTo is { } net && !TerminatingOrDeleted(GetEntity(net)))
            args.PushMarkup(Loc.GetString("oxyd-medical-iv-examine-vessel", ("vessel", Name(GetEntity(net)))));
        else
            args.PushMarkup(Loc.GetString("oxyd-medical-iv-examine-no-vessel"));
        args.PushMarkup(Loc.GetString(comp.DrainMode
            ? "oxyd-medical-iv-examine-mode-draw"
            : "oxyd-medical-iv-examine-mode-inject", ("amount", comp.TransferPerTick)));
    }

    private void OnInit(EntityUid uid, OxydIvDripComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(uid, OxydIvDripComponent.BeakerContainerId);
    }

    private ContainerSlot BeakerSlot(EntityUid uid) =>
        _container.EnsureContainer<ContainerSlot>(uid, OxydIvDripComponent.BeakerContainerId);

    private void OnCanDropOntoPatient(EntityUid uid, BodyComponent comp, ref CanDropTargetEvent args)
    {
        if (args.Handled || !HasComp<OxydIvDripComponent>(args.Dragged))
            return;
        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnDragOntoPatient(EntityUid uid, BodyComponent comp, DragDropTargetEvent args)
    {
        if (args.Handled || !TryComp<OxydIvDripComponent>(args.Dragged, out var drip))
            return;

        args.Handled = true;
        ToggleAttach(args.Dragged, drip, uid, args.User);
    }

    private void OnAttachDoAfter(Entity<OxydIvDripComponent> ent, ref OxydIvAttachDoAfterEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (args.Cancelled)
            return;

        var (uid, comp) = ent;
        if (args.Target is not { } patient || TerminatingOrDeleted(patient))
            return;
        if (comp.AttachedTo != null || !_transform.InRange(uid, patient, IvRange))
            return;

        comp.AttachedTo = GetNetEntity(patient);
        Dirty(uid, comp);
        _popup.PopupEntity(Loc.GetString("oxyd-medical-iv-attached", ("patient", Name(patient))),
            uid, args.User);
        _adminLogger.Add(LogType.ForceFeed, LogImpact.Medium,
            $"{ToPrettyString(args.User):user} attached {ToPrettyString(uid):using} to {ToPrettyString(patient):target}");
    }

    private void OnCanDropPatientOnDrip(EntityUid uid, OxydIvDripComponent comp, ref CanDropTargetEvent args)
    {
        if (args.Handled || !HasComp<BodyComponent>(args.Dragged))
            return;
        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnDropPatientOnDrip(EntityUid uid, OxydIvDripComponent comp, DragDropTargetEvent args)
    {
        if (args.Handled || !HasComp<BodyComponent>(args.Dragged))
            return;

        args.Handled = true;
        ToggleAttach(uid, comp, args.Dragged, args.User);
    }

    private void ToggleAttach(EntityUid dripUid, OxydIvDripComponent drip, EntityUid patient, EntityUid user)
    {
        if (drip.AttachedTo == GetNetEntity(patient))
        {
            Detach(dripUid, drip, user);
            return;
        }

        if (drip.AttachedTo != null || !_transform.InRange(dripUid, patient, IvRange))
            return;

        // Mirror the stock injector: inject blockers (jugsuits etc.) veto the jab
        // before the do-after even starts.
        var injectEv = new TargetBeforeInjectEvent(user, dripUid, patient);
        RaiseLocalEvent(patient, ref injectEv);
        if (injectEv.Cancelled)
        {
            var userMessage = Loc.GetString("injector-component-blocked-user");
            var otherMessage = Loc.GetString("injector-component-blocked-other",
                ("target", patient), ("user", user));
            _popup.PopupEntity(userMessage, otherMessage, patient, user, PopupType.SmallCaution);
            return;
        }

        // Attach takes a do-after like an injector jab: it is not instant and the
        // patient can move away or be damaged out of it.
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, drip.AttachDelay,
            new OxydIvAttachDoAfterEvent(), dripUid, target: patient, used: dripUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
        _popup.PopupEntity(Loc.GetString("oxyd-medical-iv-attaching", ("patient", Name(patient))),
            dripUid, user);
    }

    private void Detach(EntityUid dripUid, OxydIvDripComponent drip, EntityUid user)
    {
        var previous = drip.AttachedTo is { } net ? GetEntity(net) : (EntityUid?) null;
        drip.AttachedTo = null;
        Dirty(dripUid, drip);
        _popup.PopupEntity(Loc.GetString("oxyd-medical-iv-detached"), dripUid, user);
        if (previous is { } patient && !TerminatingOrDeleted(patient))
        {
            _adminLogger.Add(LogType.ForceFeed, LogImpact.Low,
                $"{ToPrettyString(user):user} detached {ToPrettyString(dripUid):using} from {ToPrettyString(patient):target}");
        }
    }

    private void AddIvVerbs(EntityUid uid, OxydIvDripComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (comp.AttachedTo != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => Detach(uid, comp, args.User),
                Text = Loc.GetString("oxyd-medical-iv-verb-detach"),
            });
        }

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () =>
            {
                comp.DrainMode = !comp.DrainMode;
                Dirty(uid, comp);
                _adminLogger.Add(LogType.ForceFeed, LogImpact.Low,
                    $"{ToPrettyString(args.User):user} set {ToPrettyString(uid):using} to {(comp.DrainMode ? "draw" : "inject")}");
            },
            Text = comp.DrainMode
                ? Loc.GetString("oxyd-medical-iv-verb-inject")
                : Loc.GetString("oxyd-medical-iv-verb-draw"),
        });

        // Attaching is drag-drop only (drip onto patient or patient onto drip): an
        // explicit target plus the attach do-after, never an arbitrary nearby body.

        // Eris "Set IV transfer amount": cycles through the usual drip rates.
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () =>
            {
                var rates = OxydIvDripComponent.TransferRates;
                var idx = Array.IndexOf(rates, comp.TransferPerTick);
                comp.TransferPerTick = rates[(idx + 1) % rates.Length];
                Dirty(uid, comp);
                _popup.PopupEntity(Loc.GetString("oxyd-medical-iv-amount-set",
                    ("amount", comp.TransferPerTick)), uid, args.User);
            },
            Text = Loc.GetString("oxyd-medical-iv-verb-amount", ("amount", comp.TransferPerTick)),
        });
    }

    /// <summary>Eris attackby: clicking the drip with a beaker/bloodpack loads it.</summary>
    private void OnInteractUsing(EntityUid uid, OxydIvDripComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || BeakerSlot(uid).ContainedEntity != null)
            return;
        if (!_solutions.TryGetSolution(args.Used, "beaker", out _, out _))
            return;

        args.Handled = true;
        _container.Insert(args.Used, BeakerSlot(uid));
    }

    private void AddInsertBeakerVerb(EntityUid uid, OxydIvDripComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract)
            return;

        // Eject whatever is loaded (no held item needed), or load the held beaker.
        if (BeakerSlot(uid).ContainedEntity is { } loaded)
        {
            var b = loaded;
            args.Verbs.Add(new InteractionVerb
            {
                Act = () => _container.Remove(b, BeakerSlot(uid)),
                Text = Loc.GetString("oxyd-medical-iv-verb-eject-beaker"),
            });
            return;
        }

        if (args.Using == null ||
            !_solutions.TryGetSolution(args.Using.Value, "beaker", out _, out _))
            return;

        var beaker = args.Using.Value;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () => _container.Insert(beaker, BeakerSlot(uid)),
            Text = Loc.GetString("oxyd-medical-iv-verb-insert-beaker"),
        });
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydIvDripComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.AttachedTo is not { } netPatient)
                continue;

            var now = _timing.CurTime;
            if (now < comp.NextTick)
                continue;
            comp.NextTick = now + TimeSpan.FromSeconds(comp.TickInterval);

            var patient = GetEntity(netPatient);
            if (TerminatingOrDeleted(patient) || !_transform.InRange(uid, patient, IvRange))
            {
                comp.AttachedTo = null;
                Dirty(uid, comp);
                continue;
            }

            if (BeakerSlot(uid).ContainedEntity is not { } beaker ||
                !_solutions.TryGetSolution(beaker, "beaker", out var beakerSolEnt, out var beakerSol) ||
                !_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                    out var bloodEnt, out var blood))
                continue;

            if (comp.DrainMode)
            {
                // Draw: pull blood out of the patient into the beaker. Capped by the
                // beaker's free volume inside TryTransferSolution — nothing is lost.
                var before = blood.Volume;
                if (!_solutions.TryTransferSolution(beakerSolEnt.Value, blood, comp.TransferPerTick))
                    continue;
                // TryTransferSolution refreshes the destination; the source entity
                // needs its own UpdateChemicals for the client to see it shrink.
                _solutions.UpdateChemicals(bloodEnt.Value);
                var moved = before - blood.Volume;
                _adminLogger.Add(LogType.ForceFeed, LogImpact.Low,
                    $"{ToPrettyString(uid):using} drew {moved}u from {ToPrettyString(patient):target} " +
                    $"into {ToPrettyString(beaker):used}");
            }
            else
            {
                // Inject: push beaker contents into the bloodstream, capped by the
                // bloodstream's free volume so overflow can't delete reagents.
                var before = beakerSol.Volume;
                if (!_solutions.TryTransferSolution(bloodEnt.Value, beakerSol, comp.TransferPerTick))
                    continue;
                _solutions.UpdateChemicals(beakerSolEnt.Value);
                var moved = before - beakerSol.Volume;
                _adminLogger.Add(LogType.ForceFeed, LogImpact.Low,
                    $"{ToPrettyString(uid):using} injected {ToPrettyString(patient):target} " +
                    $"with {moved}u from {ToPrettyString(beaker):used}");
            }
        }
    }
}
