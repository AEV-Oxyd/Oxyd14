using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.Medical;

public sealed partial class AddictiveEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, Addictive>
{
    [Dependency] private readonly AddictionSystem _addiction = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<Addictive> args)
    {
        _addiction.Expose(entity, args.Effect, args.Scale);
    }
}

/// <summary>Tracks dependence through exposure, satisfaction, withdrawal, and recovery.</summary>
public sealed partial class AddictionSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    [SubscribeLocalEvent]
    private void OnRejuvenate(EntityUid uid, AddictionComponent comp, RejuvenateEvent args)
    {
        comp.Reagents.Clear();
    }

    public void Expose(EntityUid uid, Addictive effect, float scale = 1f)
    {
        if (scale <= 0 || _mobs.IsDead(uid) ||
            !_solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood))
            return;

        var quantity = blood.GetTotalPrototypeQuantity(effect.Reagent);
        if (quantity <= 0)
            return;

        var comp = EnsureComp<AddictionComponent>(uid);
        if (!comp.Reagents.TryGetValue(effect.Reagent, out var state))
            comp.Reagents[effect.Reagent] = state = new ReagentDependence();

        state.PeakDose = Content.Shared.FixedPoint.FixedPoint2.Max(state.PeakDose, quantity);
        if (state.Progress != null || quantity >= effect.Threshold || _random.Prob(effect.AddictionChance * scale))
            state.Progress = -15;
    }

    /// <summary>Advances all dependencies. This does not remove reagents or cause immediate withdrawal damage.</summary>
    public void AdvanceRecovery(EntityUid uid, int amount)
    {
        if (amount <= 0 || !TryComp<AddictionComponent>(uid, out var comp))
            return;

        foreach (var (reagent, state) in comp.Reagents.ToArray())
        {
            if (state.Progress == null)
                continue;

            state.Progress += amount;
            if (state.Progress >= comp.RecoveryThreshold)
                Recover((uid, comp), reagent);
        }
    }

    private void Recover(Entity<AddictionComponent> ent, ProtoId<ReagentPrototype> reagent)
    {
        ent.Comp.Reagents.Remove(reagent);
        if (TryComp<SanityComponent>(ent, out var sanity))
            _sanity.ApplySanityDelta((ent, sanity), SanitySource.Mental, 15);

        _popup.PopupEntity(Loc.GetString("oxyd-medical-addiction-recovered",
            ("reagent", _prototypes.Index(reagent).LocalizedName)), ent, ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<AddictionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_mobs.IsDead(uid))
                continue;

            comp.UpdateRemaining -= frameTime;
            if (comp.UpdateRemaining > 0)
                continue;

            comp.UpdateRemaining = comp.UpdateInterval;
            _solutions.TryGetSolution(uid, BloodstreamComponent.DefaultBloodSolutionName, out _, out var blood);
            foreach (var (reagent, state) in comp.Reagents.ToArray())
            {
                if (blood?.GetTotalPrototypeQuantity(reagent) > 0)
                {
                    if (state.Progress != null)
                        state.Progress = -15;
                    continue;
                }

                if (state.Progress == null)
                {
                    comp.Reagents.Remove(reagent);
                    continue;
                }

                state.Progress++;
                if (state.Progress >= comp.RecoveryThreshold)
                {
                    Recover((uid, comp), reagent);
                    continue;
                }

                if (state.Progress <= 0 || !_random.Prob(0.3f))
                    continue;

                _popup.PopupEntity(Loc.GetString("oxyd-medical-addiction-craving",
                    ("reagent", _prototypes.Index(reagent).LocalizedName)), uid, uid);
                if (state.Progress > 30 && TryComp<SanityComponent>(uid, out var sanity))
                    _sanity.ApplySanityDelta((uid, sanity), SanitySource.Mental, state.Progress > 40 ? -10 : -5);
            }
        }
    }
}
