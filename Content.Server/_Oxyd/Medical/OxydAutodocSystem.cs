using System.Linq;
using Content.Server.Stack;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris Autodoc (machinery/autodoc.dm + surgery/autodoc.dm capitalist_autodoc):
/// Scan detects patchnotes of problems on the occupant; the user toggles operations per
/// organ; "Process all"/"Process picked" charge the inserted credits and run one operation
/// tick at a time, locking the pod until finished or aborted.
///
/// Billing: Eris charges personal bank accounts off ID cards (with PIN); SS14 has no
/// per-person banking, so the balance is physical SpaceCash inserted into the pod
/// (Eris's credit bills). Eject credits returns the remainder.
/// </summary>
public sealed partial class OxydAutodocSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";
    private static readonly ProtoId<DamageTypePrototype> PoisonDamage = "Poison";

    private float Group(EntityUid patient, HashSet<ProtoId<DamageTypePrototype>> types)
    {
        if (!TryComp<DamageableComponent>(patient, out var dmg))
            return 0f;
        var spec = _damage.GetPositiveDamage((patient, dmg));
        return OxydDamageTypes.Sum(spec, types);
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAutodocComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<OxydAutodocComponent, GetVerbsEvent<InteractionVerb>>(AddInsertVerb);
        SubscribeLocalEvent<OxydAutodocComponent, GetVerbsEvent<AlternativeVerb>>(AddEjectVerb);
        SubscribeLocalEvent<OxydAutodocComponent, DragDropTargetEvent>(OnDragDropOn);
        SubscribeLocalEvent<OxydAutodocComponent, CanDropTargetEvent>(OnCanDropOn);
        SubscribeLocalEvent<OxydAutodocComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OxydAutodocComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydAutodocComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydAutodocComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocScanMessage>(OnScan);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocProcessAllMessage>(OnProcessAll);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocProcessPickedMessage>(OnProcessPicked);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocAbortMessage>(OnAbort);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocToggleMessage>(OnToggle);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocEjectMessage>(OnEject);
        SubscribeLocalEvent<OxydAutodocComponent, OxydAutodocEjectCreditsMessage>(OnEjectCredits);
    }

    private void OnUiOpen(EntityUid uid, OxydAutodocComponent comp, AfterActivatableUIOpenEvent args)
    {
        PushState(uid, comp);
    }

    private void OnContainerChanged(EntityUid uid, OxydAutodocComponent comp, ContainerModifiedMessage args)
    {
        _appearance.SetData(uid, OxydMachineVisuals.Occupied, BodySlot(uid).ContainedEntity != null);
        PushState(uid, comp);
    }

    private void OnInit(EntityUid uid, OxydAutodocComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(uid, OxydAutodocComponent.BodyContainerId);
        _container.EnsureContainer<Container>(uid, OxydAutodocComponent.CreditsContainerId);
    }

    private ContainerSlot BodySlot(EntityUid uid) =>
        _container.EnsureContainer<ContainerSlot>(uid, OxydAutodocComponent.BodyContainerId);

    private Container CreditContainer(EntityUid uid) =>
        _container.EnsureContainer<Container>(uid, OxydAutodocComponent.CreditsContainerId);

    // ---------------- Credits ----------------

    private int GetBalance(EntityUid uid)
    {
        var balance = 0;
        foreach (var ent in CreditContainer(uid).ContainedEntities)
        {
            if (TryComp<StackComponent>(ent, out var stack))
                balance += stack.Count;
        }
        return balance;
    }

    /// <summary>Eris charge(): spend <paramref name="amount"/> credits from the machine's
    /// balance, or fail with the popup.</summary>
    private bool Charge(EntityUid uid, OxydAutodocComponent comp, int amount)
    {
        var balance = GetBalance(uid);
        if (amount > balance)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-autodoc-insufficient"), uid, PopupType.Small);
            return false;
        }

        var remaining = amount;
        foreach (var ent in CreditContainer(uid).ContainedEntities.ToArray())
        {
            if (remaining <= 0)
                break;
            if (!TryComp<StackComponent>(ent, out var stack))
                continue;
            var take = Math.Min(stack.Count, remaining);
            // ReduceCount reaches 0 by deleting the stack entity.
            _stack.ReduceCount((ent, stack), take);
            remaining -= take;
        }
        return true;
    }

    private void OnInteractUsing(EntityUid uid, OxydAutodocComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CashComponent>(args.Used))
            return;
        args.Handled = true;
        _container.Insert(args.Used, CreditContainer(uid));
        PushState(uid, comp);
    }

    private void OnEjectCredits(EntityUid uid, OxydAutodocComponent comp, OxydAutodocEjectCreditsMessage args)
    {
        if (comp.Operating)
            return;
        var coords = Transform(uid).Coordinates;
        foreach (var ent in CreditContainer(uid).ContainedEntities.ToArray())
        {
            _container.Remove(ent, CreditContainer(uid), reparent: false);
            _transform.SetCoordinates(ent, coords);
        }
        PushState(uid, comp);
    }

    // ---------------- Occupant in/out ----------------

    private void OnCanDropOn(EntityUid uid, OxydAutodocComponent comp, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;
        // Only claim the drop for a body we can accept so other drop handlers can still run.
        args.CanDrop = !comp.Operating && BodySlot(uid).ContainedEntity == null && HasComp<BodyComponent>(args.Dragged);
        args.Handled = args.CanDrop;
    }

    private void OnDragDropOn(EntityUid uid, OxydAutodocComponent comp, DragDropTargetEvent args)
    {
        if (args.Handled || comp.Operating || BodySlot(uid).ContainedEntity != null ||
            !HasComp<BodyComponent>(args.Dragged))
            return;

        args.Handled = true;
        _container.Insert(args.Dragged, BodySlot(uid));
        comp.Notes.Clear();
        PushState(uid, comp);
    }

    private void AddInsertVerb(EntityUid uid, OxydAutodocComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || comp.Operating || BodySlot(uid).ContainedEntity != null)
            return;

        // Insert the body the user is pulling (args.Target is the autodoc itself).
        if (!TryComp<PullerComponent>(args.User, out var puller) || puller.Pulling is not { } target ||
            !HasComp<BodyComponent>(target))
            return;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () =>
            {
                _container.Insert(target, BodySlot(uid));
                comp.Notes.Clear();
                PushState(uid, comp);
            },
            Text = Loc.GetString("oxyd-medical-sleeper-verb-insert"),
        });
    }

    private void AddEjectVerb(EntityUid uid, OxydAutodocComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        // Eris: locked = processor.active — can't eject mid-procedure.
        if (comp.Operating || BodySlot(uid).ContainedEntity == null)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => Eject(uid, comp),
            Text = Loc.GetString("oxyd-medical-sleeper-verb-eject"),
        });
    }

    private void Eject(EntityUid uid, OxydAutodocComponent comp)
    {
        if (comp.Operating)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-autodoc-locked"), uid, PopupType.Small);
            return;
        }
        if (BodySlot(uid).ContainedEntity is { } occ)
            _container.Remove(occ, BodySlot(uid));
        comp.Notes.Clear();
        _appearance.SetData(uid, OxydMachineVisuals.Working, false);
        PushState(uid, comp);
    }

    // ---------------- UI handlers ----------------

    /// <summary>Eris scan_user(): builds patchnotes for the occupant, auto-picked.</summary>
    private void OnScan(EntityUid uid, OxydAutodocComponent comp, OxydAutodocScanMessage args)
    {
        if (comp.Operating || BodySlot(uid).ContainedEntity is not { } patient)
            return;
        if (!Charge(uid, comp, comp.ScanCost))
            return;

        comp.Notes.Clear();

        // Global toxnote (Eris: no organ).
        var global = new OxydAutodocPatchnote();
        if (Group(patient, OxydDamageTypes.Toxin) > 0)
            global.Scanned.Add(OxydAutodocOp.Toxin);
        if (_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                out var bloodEnt, out var blood))
        {
            var bloodVol = blood.GetTotalPrototypeQuantity(BloodReagent).Float();
            if (blood.Volume.Float() > bloodVol) // anything in the blood besides blood
                global.Scanned.Add(OxydAutodocOp.Dialysis);
            if (bloodVol < blood.MaxVolume.Float() * 0.95f)
                global.Scanned.Add(OxydAutodocOp.Blood);
        }
        if (global.Scanned.Count > 0)
        {
            global.Picked.UnionWith(global.Scanned);
            comp.Notes.Add(global);
        }

        foreach (var (orgUid, organ, surg) in _wounds.GetOrgans(patient))
        {
            var note = new OxydAutodocPatchnote { Organ = orgUid };
            if (OxydWoundSystem.IsExternal(organ))
            {
                if (surg.OrganDamage > 0)
                    note.Scanned.Add(OxydAutodocOp.Damage);
                if (surg.Fractured)
                    note.Scanned.Add(OxydAutodocOp.Fracture);
                if (surg.EmbeddedItems.Count > 0)
                    note.Scanned.Add(OxydAutodocOp.Shrapnel);
                if (surg.Incision != OxydIncisionStage.None || surg.WoundBleedRate > 0)
                    note.Scanned.Add(OxydAutodocOp.OpenWounds);
            }
            else if (surg.OrganDamage > 0)
            {
                note.Scanned.Add(OxydAutodocOp.InternalWounds);
            }

            if (note.Scanned.Count == 0)
                continue;
            note.Picked.UnionWith(note.Scanned);
            comp.Notes.Add(note);
        }

        _appearance.SetData(uid, OxydMachineVisuals.Working, false);
        PushState(uid, comp);
    }

    private void OnProcessAll(EntityUid uid, OxydAutodocComponent comp, OxydAutodocProcessAllMessage args)
    {
        if (comp.Operating || BodySlot(uid).ContainedEntity == null || comp.Notes.Count == 0)
            return;
        foreach (var note in comp.Notes)
            note.Picked.UnionWith(note.Scanned);
        StartProcessing(uid, comp, TotalCost(comp));
    }

    private void OnProcessPicked(EntityUid uid, OxydAutodocComponent comp, OxydAutodocProcessPickedMessage args)
    {
        if (comp.Operating || BodySlot(uid).ContainedEntity == null || comp.Notes.Count == 0)
            return;
        StartProcessing(uid, comp, CustomCost(comp));
    }

    private void StartProcessing(EntityUid uid, OxydAutodocComponent comp, int cost)
    {
        if (cost <= 0)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-autodoc-nothing"), uid, PopupType.Small);
            return;
        }
        if (!Charge(uid, comp, cost))
            return;

        comp.Operating = true;
        comp.OpsTotal = comp.Notes.Sum(n => n.Picked.Count);
        comp.NextOpTime = _timing.CurTime + TimeSpan.FromSeconds(comp.StepDuration);
        _appearance.SetData(uid, OxydMachineVisuals.Working, true);
        Dirty(uid, comp);
        PushState(uid, comp);
    }

    private void OnAbort(EntityUid uid, OxydAutodocComponent comp, OxydAutodocAbortMessage args)
    {
        // Eris stop(): halts the run and clears the picked operations.
        comp.Operating = false;
        _appearance.SetData(uid, OxydMachineVisuals.Working, false);
        foreach (var note in comp.Notes)
            note.Picked.Clear();
        Dirty(uid, comp);
        PushState(uid, comp);
    }

    private void OnToggle(EntityUid uid, OxydAutodocComponent comp, OxydAutodocToggleMessage args)
    {
        if (comp.Operating || args.EntryId < 0 || args.EntryId >= comp.Notes.Count)
            return;
        var note = comp.Notes[args.EntryId];
        if (!note.Scanned.Contains(args.Op))
            return;
        if (!note.Picked.Remove(args.Op))
            note.Picked.Add(args.Op);
        PushState(uid, comp);
    }

    private void OnEject(EntityUid uid, OxydAutodocComponent comp, OxydAutodocEjectMessage args)
    {
        Eject(uid, comp);
    }

    // ---------------- Processing ----------------

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydAutodocComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Operating)
                continue;

            if (BodySlot(uid).ContainedEntity is not { } occupant || TerminatingOrDeleted(occupant))
            {
                comp.Operating = false;
                comp.Notes.Clear();
                _appearance.SetData(uid, OxydMachineVisuals.Working, false);
                Dirty(uid, comp);
                continue;
            }

            var now = _timing.CurTime;
            if (now < comp.NextOpTime)
                continue;

            // First note that still has picked ops; Eris advances one operation per tick.
            var note = comp.Notes.FirstOrDefault(n => n.Picked.Count > 0);
            if (note == null)
            {
                comp.Operating = false;
                _appearance.SetData(uid, OxydMachineVisuals.Working, false);
                _popup.PopupEntity(Loc.GetString("oxyd-medical-autodoc-done"), uid, PopupType.Small);
                Dirty(uid, comp);
                PushState(uid, comp);
                continue;
            }

            comp.NextOpTime = now + TimeSpan.FromSeconds(comp.StepDuration);
            var op = note.Picked.First();
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(uid):actor} autodoc applied {op} to {ToPrettyString(occupant):target} (dose {note.Picked.Count})");
            ProcessOp(uid, comp, occupant, note, op);
            PushState(uid, comp);
        }
    }

    /// <summary>Eris process_note(): one operation dose; clears the op when its job is done.</summary>
    private void ProcessOp(EntityUid uid, OxydAutodocComponent comp, EntityUid patient,
        OxydAutodocPatchnote note, OxydAutodocOp op)
    {
        switch (op)
        {
            case OxydAutodocOp.Toxin:
                var heal = new DamageSpecifier();
                heal.DamageDict.TryAdd(PoisonDamage, -comp.HealPerTick);
                _damage.TryChangeDamage(patient, heal);
                if (Group(patient, OxydDamageTypes.Toxin) <= 0)
                    note.Picked.Remove(op);
                break;

            case OxydAutodocOp.Dialysis:
                if (_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                        out var bloodEnt, out var blood))
                {
                    // Eris removes AUTODOC_DIALYSIS_AMOUNT per reagent + a bit of blood.
                    var purged = _solutions.RemoveEachReagent(bloodEnt.Value, comp.DialysisPerTick);
                    var bloodLeft = blood.GetTotalPrototypeQuantity(BloodReagent).Float();
                    if (purged.Volume <= 0 || blood.Volume.Float() - bloodLeft <= 0)
                        note.Picked.Remove(op);
                }
                else
                {
                    note.Picked.Remove(op);
                }
                break;

            case OxydAutodocOp.Blood:
                if (_solutions.TryGetSolution(patient, BloodstreamComponent.DefaultBloodSolutionName,
                        out var bloodEnt2, out var blood2))
                {
                    var missing = blood2.MaxVolume.Float() - blood2.Volume.Float();
                    if (missing <= 0)
                    {
                        note.Picked.Remove(op);
                    }
                    else
                    {
                        _solutions.TryAddReagent(bloodEnt2.Value, BloodReagent, Math.Min(comp.HealPerTick, missing));
                    }
                }
                else
                {
                    note.Picked.Remove(op);
                }
                break;

            default:
                if (note.Organ is not { } organUid || !TryComp<OxydOrganSurgeryComponent>(organUid, out var surg))
                {
                    note.Picked.Remove(op);
                    break;
                }
                switch (op)
                {
                    case OxydAutodocOp.Damage:
                    case OxydAutodocOp.InternalWounds:
                        // Eris heals damage_heal_amount per tick until the organ is clean.
                        OxydWoundSystem.ReduceOrganDamage(surg, comp.HealPerTick);
                        if (surg.OrganDamage <= 0)
                            note.Picked.Remove(op);
                        break;
                    case OxydAutodocOp.OpenWounds:
                        surg.Clamped = true;
                        surg.Incision = OxydIncisionStage.None;
                        surg.WoundBleedRate = 0;
                        note.Picked.Remove(op);
                        break;
                    case OxydAutodocOp.Fracture:
                        surg.Fractured = false;
                        surg.Splinted = false;
                        note.Picked.Remove(op);
                        break;
                    case OxydAutodocOp.Shrapnel:
                        foreach (var net in surg.EmbeddedItems.ToArray())
                        {
                            var item = GetEntity(net);
                            surg.EmbeddedItems.Remove(net);
                            _transform.SetCoordinates(item, Transform(uid).Coordinates);
                        }
                        note.Picked.Remove(op);
                        break;
                    default:
                        note.Picked.Remove(op);
                        break;
                }
                Dirty(organUid, surg);
                break;
        }
    }

    private int TotalCost(OxydAutodocComponent comp) =>
        comp.Notes.Sum(n => n.Scanned.Sum(op => CostOf(comp, op)));

    private int CustomCost(OxydAutodocComponent comp) =>
        comp.Notes.Sum(n => n.Picked.Sum(op => CostOf(comp, op)));

    private int CostOf(OxydAutodocComponent comp, OxydAutodocOp op) =>
        comp.OpCosts.GetValueOrDefault(op);

    private void PushState(EntityUid uid, OxydAutodocComponent comp)
    {
        var state = new OxydAutodocState
        {
            Running = comp.Operating,
            Balance = GetBalance(uid),
            ScanCost = comp.ScanCost,
            TotalCost = TotalCost(comp),
            CustomCost = CustomCost(comp),
            OpCosts = new Dictionary<OxydAutodocOp, int>(comp.OpCosts),
        };

        // Progress over the ops selected at start (picked shrinks as ops complete).
        state.Progress = comp.OpsTotal > 0
            ? 1f - (float) comp.Notes.Sum(n => n.Picked.Count) / comp.OpsTotal
            : 0f;

        if (BodySlot(uid).ContainedEntity is { } occ)
        {
            state.HasOccupant = true;
            state.OccupantName = Name(occ);
            state.BruteLoss = Group(occ, OxydDamageTypes.Brute);
            state.BurnLoss = Group(occ, OxydDamageTypes.Burn);
            state.ToxinLoss = Group(occ, OxydDamageTypes.Toxin);
            state.OxyLoss = Group(occ, OxydDamageTypes.Airloss);
            if (_solutions.TryGetSolution(occ, BloodstreamComponent.DefaultBloodSolutionName,
                    out _, out var blood))
            {
                var vol = blood.GetTotalPrototypeQuantity(BloodReagent).Float();
                state.BloodPercent = blood.MaxVolume.Float() > 0
                    ? vol / blood.MaxVolume.Float() * 100f : 0f;
            }
        }

        for (var i = 0; i < comp.Notes.Count; i++)
        {
            var note = comp.Notes[i];
            var entry = new OxydAutodocEntry
            {
                Id = i,
                Global = note.Organ == null,
                Available = note.Scanned.ToList(),
                Picked = note.Picked.ToList(),
            };
            if (note.Organ is { } orgUid)
            {
                entry.Name = Name(orgUid);
                if (TryComp<OxydOrganSurgeryComponent>(orgUid, out var surg))
                {
                    entry.Internal = !(TryComp<OrganComponent>(orgUid, out var oc) && OxydWoundSystem.IsExternal(oc));
                    entry.BruteDamage = surg.BruteDamage;
                    entry.BurnDamage = surg.BurnDamage;
                    entry.InnerDamage = surg.OrganDamage;
                }
            }
            if (entry.Global)
                state.Global = entry;
            else
                state.Organs.Add(entry);
        }

        _ui.SetUiState(uid, OxydAutodocUiKey.Key, state);
    }
}
