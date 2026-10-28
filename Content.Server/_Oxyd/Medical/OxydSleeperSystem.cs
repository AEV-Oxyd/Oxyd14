using Content.Shared.Damage.Components;
using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris sleeper pod (machinery/Sleeper.dm): occupied via drag/verb, injects from a
/// fixed chem menu into the occupant's bloodstream, with a beaker slot for dialysis output.
/// </summary>
public sealed partial class OxydSleeperSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private float _refreshRemaining;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydSleeperComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<OxydSleeperComponent, GetVerbsEvent<InteractionVerb>>(AddInsertVerb);
        SubscribeLocalEvent<OxydSleeperComponent, GetVerbsEvent<AlternativeVerb>>(AddEjectVerb);
        SubscribeLocalEvent<OxydSleeperComponent, DragDropTargetEvent>(OnDragDropOn);
        SubscribeLocalEvent<OxydSleeperComponent, CanDropTargetEvent>(OnCanDropOn);
        SubscribeLocalEvent<OxydSleeperComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OxydSleeperComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<OxydSleeperComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydSleeperComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydSleeperComponent, OxydSleeperInjectMessage>(OnInject);
        SubscribeLocalEvent<OxydSleeperComponent, OxydSleeperEjectMessage>(OnEject);
        SubscribeLocalEvent<OxydSleeperComponent, OxydSleeperEjectBeakerMessage>(OnEjectBeaker);
        SubscribeLocalEvent<OxydSleeperComponent, OxydSleeperToggleFilterMessage>(OnToggleFilter);
    }

    private void OnInit(EntityUid uid, OxydSleeperComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(uid, OxydSleeperComponent.BodyContainerId);
        _container.EnsureContainer<ContainerSlot>(uid, OxydSleeperComponent.BeakerContainerId);
    }

    private ContainerSlot BodySlot(EntityUid uid) =>
        _container.EnsureContainer<ContainerSlot>(uid, OxydSleeperComponent.BodyContainerId);

    private ContainerSlot BeakerSlot(EntityUid uid) =>
        _container.EnsureContainer<ContainerSlot>(uid, OxydSleeperComponent.BeakerContainerId);

    private void OnCanDropOn(EntityUid uid, OxydSleeperComponent comp, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;
        // Only claim the drop when it's a body we can accept, so other drop
        // handlers (e.g. beaker insertion) can still run for anything else.
        args.CanDrop = BodySlot(uid).ContainedEntity == null && HasComp<BodyComponent>(args.Dragged);
        args.Handled = args.CanDrop;
    }

    private void OnDragDropOn(EntityUid uid, OxydSleeperComponent comp, DragDropTargetEvent args)
    {
        if (args.Handled || BodySlot(uid).ContainedEntity != null || !HasComp<BodyComponent>(args.Dragged))
            return;

        args.Handled = true;
        _container.Insert(args.Dragged, BodySlot(uid));
    }

    private void AddInsertVerb(EntityUid uid, OxydSleeperComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract)
            return;

        // Insert a held beaker for dialysis output (Eris loads the beaker by clicking).
        if (args.Using is { } used && BeakerSlot(uid).ContainedEntity == null &&
            _solutions.TryGetSolution(used, "beaker", out _, out _))
        {
            args.Verbs.Add(new InteractionVerb
            {
                Act = () => _container.Insert(used, BeakerSlot(uid)),
                Text = Loc.GetString("oxyd-medical-sleeper-verb-insert-beaker"),
            });
        }

        // Insert the body the user is pulling (args.Target is the sleeper itself).
        if (BodySlot(uid).ContainedEntity != null ||
            !TryComp<PullerComponent>(args.User, out var puller) || puller.Pulling is not { } target ||
            !HasComp<BodyComponent>(target))
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Act = () => _container.Insert(target, BodySlot(uid)),
            Text = Loc.GetString("oxyd-medical-sleeper-verb-insert"),
        });
    }

    /// <summary>Eris: clicking the sleeper with a beaker loads it into the dialysis slot.</summary>
    private void OnInteractUsing(EntityUid uid, OxydSleeperComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || BeakerSlot(uid).ContainedEntity != null)
            return;
        if (!_solutions.TryGetSolution(args.Used, "beaker", out _, out _))
            return;

        args.Handled = true;
        _container.Insert(args.Used, BeakerSlot(uid));
    }

    private void AddEjectVerb(EntityUid uid, OxydSleeperComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (BodySlot(uid).ContainedEntity is not { } occupant)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => EjectBody(uid, occupant),
            Text = Loc.GetString("oxyd-medical-sleeper-verb-eject"),
        });
    }

    private void EjectBody(EntityUid uid, EntityUid occupant)
    {
        _container.Remove(occupant, BodySlot(uid));
    }

    private void OnUiOpen(EntityUid uid, OxydSleeperComponent comp, AfterActivatableUIOpenEvent args)
        => PushState(uid, comp);

    private void OnContainerChanged(EntityUid uid, OxydSleeperComponent comp, ContainerModifiedMessage args)
    {
        // Eris: dialysis requires both an occupant and a beaker; losing either stops it.
        if (BodySlot(uid).ContainedEntity == null || BeakerSlot(uid).ContainedEntity == null)
            comp.Filtering = false;
        _appearance.SetData(uid, OxydMachineVisuals.Occupied, BodySlot(uid).ContainedEntity != null);
        PushState(uid, comp);
    }

    private void OnInject(EntityUid uid, OxydSleeperComponent comp, OxydSleeperInjectMessage args)
    {
        if (BodySlot(uid).ContainedEntity is not { } occupant)
            return;

        var chem = comp.Chems.FirstOrDefault(c => c.Reagent == args.Reagent);
        if (chem == null)
            return;

        if (!_solutions.TryGetSolution(occupant, BloodstreamComponent.DefaultBloodSolutionName,
                out var solEnt, out var sol))
            return;

        var inPatient = sol.GetTotalPrototypeQuantity(chem.Reagent).Float();
        if (inPatient >= chem.MaxInPatient)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-sleeper-chem-max",
                ("chem", chem.Name != null ? Loc.GetString(chem.Name) : chem.Reagent)), uid, uid);
            return;
        }

        var dose = Math.Min(args.Dose > 0 ? args.Dose : chem.Dose, chem.Dose);
        _solutions.TryAddReagent(solEnt.Value, chem.Reagent, dose);
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):user} injected {dose}u of {chem.Reagent} into {ToPrettyString(occupant):target} via {ToPrettyString(uid)}");
        PushState(uid, comp);
    }

    private void OnEject(EntityUid uid, OxydSleeperComponent comp, OxydSleeperEjectMessage args)
    {
        if (BodySlot(uid).ContainedEntity is { } occupant)
            EjectBody(uid, occupant);
    }

    private void OnEjectBeaker(EntityUid uid, OxydSleeperComponent comp, OxydSleeperEjectBeakerMessage args)
    {
        comp.Filtering = false;
        if (BeakerSlot(uid).ContainedEntity is { } beaker)
            _container.Remove(beaker, BeakerSlot(uid));
    }

    private void OnToggleFilter(EntityUid uid, OxydSleeperComponent comp, OxydSleeperToggleFilterMessage args)
    {
        // Eris toggle_filter: needs an occupant and a beaker, otherwise snaps back to off.
        comp.Filtering = !comp.Filtering
                         && BodySlot(uid).ContainedEntity != null
                         && BeakerSlot(uid).ContainedEntity != null;
        PushState(uid, comp);
    }

    /// <summary>Eris Process(): while filtering, move DialysisRate of every bloodstream reagent
    /// (blood included) into the beaker; auto-stop when the beaker is full.</summary>
    private void RunDialysis(EntityUid uid, OxydSleeperComponent comp)
    {
        if (!comp.Filtering)
            return;
        if (BodySlot(uid).ContainedEntity is not { } occupant ||
            BeakerSlot(uid).ContainedEntity is not { } beaker)
        {
            comp.Filtering = false;
            return;
        }
        if (!_solutions.TryGetSolution(occupant, BloodstreamComponent.DefaultBloodSolutionName,
                out var bloodEnt, out var blood) ||
            !_solutions.TryGetSolution(beaker, "beaker", out var beakerEnt, out var beakerSol))
            return;

        if (beakerSol.AvailableVolume <= 0 || blood.Volume <= 0)
        {
            comp.Filtering = false;
            PushState(uid, comp);
            return;
        }

        // Eris pumps 3u of *each* bloodstream reagent per tick (blood included) into the beaker.
        var moved = _solutions.RemoveEachReagent(bloodEnt.Value, comp.DialysisRate);
        if (moved.Volume > 0)
            _solutions.TryAddSolution(beakerEnt.Value, moved);

        if (beakerSol.AvailableVolume <= 0)
            comp.Filtering = false;
        PushState(uid, comp);
    }

    private void PushState(EntityUid uid, OxydSleeperComponent comp)
    {
        var state = new OxydSleeperState();
        var occupant = BodySlot(uid).ContainedEntity;

        if (occupant is { } occ)
        {
            state.HasOccupant = true;
            state.OccupantName = Name(occ);
            state.OccupantCritical = _mobs.IsCritical(occ);
            state.Alive = _mobs.IsAlive(occ);
            if (TryComp<DamageableComponent>(occ, out var dmg))
            {
                var spec = _damage.GetPositiveDamage((occ, dmg));
                var damage = spec.GetTotal().Float();
                var crit = _mobThreshold.GetThresholdForState(occ, Content.Shared.Mobs.MobState.Critical).Float();
                state.OccupantHealth = crit > 0
                    ? Math.Clamp(100f - damage / crit * 100f, 0f, 100f)
                    : Math.Max(0f, 100f - damage);

                // Eris occupied view: one displayBar per damage group.
                state.BruteLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Brute);
                state.BurnLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Burn);
                state.ToxinLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Toxin);
                state.OxyLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Airloss);
            }

            // Eris "Organ Health" row: worst organ damage as a percentage of healthy.
            var organHealth = 100f;
            foreach (var (_, _, surg) in _wounds.GetOrgans(occ))
            {
                organHealth = Math.Min(organHealth, Math.Max(0f,
                    100f - surg.OrganDamage / OxydOrganSurgeryComponent.OrganMaxDamage * 100f));
            }
            state.OrganHealth = organHealth;
            state.Pulse = _wounds.ClassifyPulse(occ, state.OccupantCritical, !state.Alive);

            var bloodSol = _solutions.TryGetSolution(occ, BloodstreamComponent.DefaultBloodSolutionName,
                out _, out var sol) ? sol : null;

            foreach (var chem in comp.Chems)
            {
                var inPatient = bloodSol?.GetTotalPrototypeQuantity(chem.Reagent).Float() ?? 0f;
                state.Chems.Add(new OxydSleeperChem
                {
                    Reagent = chem.Reagent,
                    Name = chem.Name ?? chem.Reagent,
                    InPatient = inPatient,
                    DoseSize = chem.Dose,
                    Enabled = inPatient < chem.MaxInPatient && _mobs.IsAlive(occ),
                    DisabledReason = inPatient >= chem.MaxInPatient
                        ? Loc.GetString("oxyd-medical-sleeper-chem-saturated")
                        : null,
                });
            }
        }

        if (BeakerSlot(uid).ContainedEntity is { } beaker)
        {
            state.HasBeaker = true;
            if (_solutions.TryGetSolution(beaker, "beaker", out _, out var bsol))
            {
                state.BeakerVolume = bsol.Volume.Float();
                state.BeakerMaxVolume = bsol.MaxVolume.Float();
            }
        }

        state.Filtering = comp.Filtering;
        state.FilterAvailable = state.HasOccupant && state.HasBeaker;

        _ui.SetUiState(uid, OxydSleeperUiKey.Key, state);
    }

    public override void Update(float frameTime)
    {
        _refreshRemaining -= frameTime;
        if (_refreshRemaining > 0)
            return;
        _refreshRemaining = 1.5f;

        var query = EntityQueryEnumerator<OxydSleeperComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            RunDialysis(uid, comp);
            if (BodySlot(uid).ContainedEntity == null && !_ui.IsUiOpen(uid, OxydSleeperUiKey.Key))
                continue;
            PushState(uid, comp);
        }
    }
}
