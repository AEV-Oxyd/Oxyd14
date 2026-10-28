using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris centrifuge (machinery/centrifuge.dm) and electrolyzer (electrolyzer.dm):
/// centrifuge separates marked reagents from a main beaker into up to 3 separation beakers;
/// electrolyzer runs decomposition reactions on the inserted beaker's contents.
/// </summary>
public sealed partial class OxydChemProcessorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydChemProcessorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<OxydChemProcessorComponent, GetVerbsEvent<InteractionVerb>>(AddBeakerVerbs);
        SubscribeLocalEvent<OxydChemProcessorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OxydChemProcessorComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<OxydChemProcessorComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydChemProcessorComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<OxydChemProcessorComponent, OxydChemProcessorSetTargetMessage>(OnSetTarget);
        SubscribeLocalEvent<OxydChemProcessorComponent, OxydChemProcessorStartMessage>(OnStart);
        SubscribeLocalEvent<OxydChemProcessorComponent, OxydChemProcessorSetDurationMessage>(OnSetDuration);
        SubscribeLocalEvent<OxydChemProcessorComponent, OxydChemProcessorSetRunningMessage>(OnSetRunning);
        SubscribeLocalEvent<OxydChemProcessorComponent, OxydChemProcessorEjectMessage>(OnEject);
    }

    private void OnInit(EntityUid uid, OxydChemProcessorComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(uid, OxydChemProcessorComponent.MainBeakerId);
        for (var i = 0; i < OxydChemProcessorComponent.SeparationBeakerCount; i++)
            _container.EnsureContainer<ContainerSlot>(uid, SepId(i));
    }

    private static string SepId(int i) => $"{OxydChemProcessorComponent.SepBeakerIdPrefix}{i}";

    private ContainerSlot Slot(EntityUid uid, string id) =>
        _container.EnsureContainer<ContainerSlot>(uid, id);

    private void OnUiOpen(EntityUid uid, OxydChemProcessorComponent comp, AfterActivatableUIOpenEvent args)
        => PushState(uid, comp);

    private void OnContainerChanged(EntityUid uid, OxydChemProcessorComponent comp, ContainerModifiedMessage args)
        => PushState(uid, comp);

    /// <summary>Main beaker first, then the first free separation slot (Eris insert order).</summary>
    private string? FindFreeSlot(EntityUid uid)
    {
        if (Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity == null)
            return OxydChemProcessorComponent.MainBeakerId;
        for (var i = 0; i < OxydChemProcessorComponent.SeparationBeakerCount; i++)
        {
            if (Slot(uid, SepId(i)).ContainedEntity == null)
                return SepId(i);
        }
        return null;
    }

    /// <summary>Eris attackby: clicking the machine with a beaker loads it.</summary>
    private void OnInteractUsing(EntityUid uid, OxydChemProcessorComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || comp.Working || FindFreeSlot(uid) is not { } slotId)
            return;
        if (!_solutions.TryGetSolution(args.Used, "beaker", out _, out _))
            return;

        args.Handled = true;
        _container.Insert(args.Used, Slot(uid, slotId));
    }

    private void AddBeakerVerbs(EntityUid uid, OxydChemProcessorComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || args.Using is not { } used || comp.Working)
            return;

        if (!_solutions.TryGetSolution(used, "beaker", out _, out _))
            return;

        if (FindFreeSlot(uid) is not { } slotId)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Act = () => _container.Insert(used, Slot(uid, slotId)),
            Text = Loc.GetString("oxyd-medical-processor-insert-beaker"),
        });
    }

    private void OnSetTarget(EntityUid uid, OxydChemProcessorComponent comp, OxydChemProcessorSetTargetMessage args)
    {
        if (comp.Mode != OxydChemProcessorMode.Centrifuge || comp.Working)
            return;
        comp.Targets[args.Reagent] = args.TargetBeaker;
        PushState(uid, comp);
    }

    /// <summary>First loaded separation beaker (Eris' single secondary), or null.</summary>
    private Entity<SolutionComponent>? FindSepBeaker(EntityUid uid, out Solution? sepSol)
    {
        sepSol = null;
        for (var i = 0; i < OxydChemProcessorComponent.SeparationBeakerCount; i++)
        {
            if (Slot(uid, SepId(i)).ContainedEntity is { } sep &&
                _solutions.TryGetSolution(sep, "beaker", out var sEnt, out var s))
            {
                sepSol = s;
                return sEnt;
            }
        }
        return null;
    }

    private void OnStart(EntityUid uid, OxydChemProcessorComponent comp, OxydChemProcessorStartMessage args)
    {
        if (comp.Working || Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity == null)
            return;

        // Eris electrolyzer needs a separation beaker for the overflow reactants.
        if (comp.Mode == OxydChemProcessorMode.Electrolyzer && FindSepBeaker(uid, out _) == null)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-processor-need-sep-beaker"), uid, args.Actor);
            return;
        }

        comp.Working = true;
        _appearance.SetData(uid, OxydMachineVisuals.Working, true);
        comp.WorkEnd = _timing.CurTime + TimeSpan.FromSeconds(comp.WorkDuration);
        Dirty(uid, comp);
        PushState(uid, comp);
    }

    /// <summary>Eris centrifuge: pick the spin-cycle duration (5/10/15/30/60 radios).</summary>
    private void OnSetDuration(EntityUid uid, OxydChemProcessorComponent comp, OxydChemProcessorSetDurationMessage args)
    {
        if (comp.Mode != OxydChemProcessorMode.Centrifuge || comp.Working)
            return;
        comp.WorkDuration = Math.Clamp(args.Seconds, 1f, 60f);
        PushState(uid, comp);
    }

    /// <summary>Eris electrolyzer On/Off links: On starts continuous processing until
    /// Off is pressed (or nothing is left to decompose).</summary>
    private void OnSetRunning(EntityUid uid, OxydChemProcessorComponent comp, OxydChemProcessorSetRunningMessage args)
    {
        if (comp.Mode != OxydChemProcessorMode.Electrolyzer)
            return;
        comp.Continuous = args.Running;
        Dirty(uid, comp);
        if (args.Running)
            OnStart(uid, comp, new OxydChemProcessorStartMessage());
        PushState(uid, comp);
    }

    private void OnEject(EntityUid uid, OxydChemProcessorComponent comp, OxydChemProcessorEjectMessage args)
    {
        // Reject out-of-range indexes: EnsureContainer would happily create a phantom slot.
        if (comp.Working || args.BeakerIndex >= OxydChemProcessorComponent.SeparationBeakerCount)
            return;

        var slotId = args.BeakerIndex < 0
            ? OxydChemProcessorComponent.MainBeakerId
            : SepId(args.BeakerIndex);

        if (Slot(uid, slotId).ContainedEntity is { } beaker)
            _container.Remove(beaker, Slot(uid, slotId));

        PushState(uid, comp);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OxydChemProcessorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Working || _timing.CurTime < comp.WorkEnd)
                continue;

            comp.Working = false;
            _appearance.SetData(uid, OxydMachineVisuals.Working, false);
            switch (comp.Mode)
            {
                case OxydChemProcessorMode.Centrifuge:
                    RunCentrifuge(uid, comp);
                    break;
                case OxydChemProcessorMode.Electrolyzer:
                    RunElectrolyzer(uid, comp);
                    break;
            }

            // Eris electrolyzer: while switched "On" keep cycling until the main beaker
            // has no decomposable reagent left (or the separation beaker is gone/full).
            if (comp.Continuous && comp.Mode == OxydChemProcessorMode.Electrolyzer &&
                Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity is { } contMain &&
                _solutions.TryGetSolution(contMain, "beaker", out _, out var contSol) &&
                FindSepBeaker(uid, out _) != null)
            {
                var anyWork = contSol.Contents.Any(r => _prototypes.EnumeratePrototypes<ReactionPrototype>()
                    .Any(rx => rx.Products.ContainsKey(r.Reagent.Prototype) &&
                               rx.Reactants.Count(p => !p.Value.Catalyst) >= 2));
                if (anyWork)
                {
                    comp.Working = true;
                    _appearance.SetData(uid, OxydMachineVisuals.Working, true);
                    comp.WorkEnd = _timing.CurTime + TimeSpan.FromSeconds(comp.WorkDuration);
                }
                else
                    comp.Continuous = false;
            }
            else if (comp.Mode == OxydChemProcessorMode.Electrolyzer && !comp.Working)
                comp.Continuous = false;

            Dirty(uid, comp);
            PushState(uid, comp);
        }
    }

    private void RunCentrifuge(EntityUid uid, OxydChemProcessorComponent comp)
    {
        if (Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity is not { } main ||
            !_solutions.TryGetSolution(main, "beaker", out var mainEnt, out var mainSol))
            return;

        foreach (var (reagent, target) in comp.Targets)
        {
            if (target < 0 || target >= OxydChemProcessorComponent.SeparationBeakerCount)
                continue;
            if (Slot(uid, SepId(target)).ContainedEntity is not { } sep ||
                !_solutions.TryGetSolution(sep, "beaker", out var sepEnt, out var sepSol))
                continue;

            var qty = mainSol.GetTotalPrototypeQuantity(reagent);
            if (qty <= 0)
                continue;

            var moved = _solutions.RemoveReagent(mainEnt.Value, reagent, Math.Min(qty.Float(), sepSol.AvailableVolume.Float()));
            _solutions.TryAddReagent(sepEnt.Value, reagent, moved);
        }
    }

    /// <summary>
    /// Eris electrolyzer (machinery/electrolyzer.dm): decomposes the first reagent that has a
    /// known synthesis recipe. The first reactant goes back into the main beaker and the rest go
    /// into the first loaded separation beaker — splitting the output is what stops the reactants
    /// from instantly re-synthesizing (SS14 solutions auto-react on add, like Eris').
    /// </summary>
    private void RunElectrolyzer(EntityUid uid, OxydChemProcessorComponent comp)
    {
        if (Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity is not { } beaker ||
            !_solutions.TryGetSolution(beaker, "beaker", out var beakerEnt, out var sol))
            return;

        // Start already validates this, but stay defensive for mid-work beaker loss.
        if (FindSepBeaker(uid, out var sepSol) is not { } sepSolnEnt || sepSol == null)
            return;

        // One reagent per work cycle — Eris decomposes only the first reagent with a recipe.
        foreach (var (reagentId, qty) in sol.Contents.ToArray())
        {
            var reaction = _prototypes.EnumeratePrototypes<ReactionPrototype>()
                .FirstOrDefault(r => r.Products.ContainsKey(reagentId.Prototype));
            if (reaction == null)
                continue;

            var reactants = reaction.Reactants.Where(p => !p.Value.Catalyst).ToList();
            // Needs 2+ reactants so the overflow has somewhere to go; a single reactant would
            // sit in the same beaker as its product and instantly re-synthesize.
            if (reactants.Count < 2)
                continue;

            var produced = reaction.Products[reagentId.Prototype].Float();
            var outPerBatch = reactants.Skip(1).Sum(p => p.Value.Amount.Float());
            var batches = Math.Min(qty.Float() / produced,
                sepSol.AvailableVolume.Float() / outPerBatch);
            var volumeToHandle = batches * produced;
            if (volumeToHandle <= 0)
                return; // separation beaker full — nothing more to do

            _solutions.RemoveReagent(beakerEnt.Value, reagentId.Prototype, volumeToHandle);
            _solutions.TryAddReagent(beakerEnt.Value, reactants[0].Key,
                reactants[0].Value.Amount.Float() * batches);
            foreach (var p in reactants.Skip(1))
                _solutions.TryAddReagent(sepSolnEnt, p.Key, p.Value.Amount.Float() * batches);
            return;
        }
    }

    private void PushState(EntityUid uid, OxydChemProcessorComponent comp)
    {
        var state = new OxydChemProcessorState
        {
            Mode = comp.Mode,
            Working = comp.Working,
            Duration = comp.WorkDuration,
            Continuous = comp.Continuous,
        };

        if (Slot(uid, OxydChemProcessorComponent.MainBeakerId).ContainedEntity is { } main &&
            _solutions.TryGetSolution(main, "beaker", out _, out var mainSol))
        {
            state.HasMainBeaker = true;
            foreach (var (reagent, qty) in mainSol.Contents)
            {
                state.MainContents.Add(new OxydChemProcessorReagent
                {
                    Id = reagent.Prototype,
                    Name = _prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var p)
                        ? p.LocalizedName
                        : reagent.Prototype,
                    Volume = qty.Float(),
                    TargetBeaker = comp.Targets.TryGetValue(reagent.Prototype, out var t) ? t : -1,
                });
            }
        }

        for (var i = 0; i < OxydChemProcessorComponent.SeparationBeakerCount; i++)
        {
            var beaker = new OxydChemProcessorBeaker();
            if (Slot(uid, SepId(i)).ContainedEntity is { } ent &&
                _solutions.TryGetSolution(ent, "beaker", out _, out var s))
            {
                beaker.Present = true;
                beaker.Volume = s.Volume.Float();
                beaker.MaxVolume = s.MaxVolume.Float();
                foreach (var (reagent, qty) in s.Contents)
                {
                    beaker.Contents.Add(new OxydChemProcessorReagent
                    {
                        Id = reagent.Prototype,
                        Name = _prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var p)
                            ? p.LocalizedName
                            : reagent.Prototype,
                        Volume = qty.Float(),
                    });
                }
            }
            state.SeparationBeakers.Add(beaker);
        }

        _ui.SetUiState(uid, OxydChemProcessorUiKey.Key, state);
    }
}
