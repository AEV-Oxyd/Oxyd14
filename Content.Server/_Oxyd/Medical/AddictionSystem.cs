using System.IO;
using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.Sanity;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Prototypes;
using Content.Shared.Random.Helpers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

public sealed partial class AddictiveEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, Addictive>
{
    [Dependency] private AddictionSystem _addiction = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The Addictive effect restates its containing reagent's id; catch drift
        // between the two at init rather than metabolizing the wrong reagent.
        foreach (var reagent in _prototypes.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.Metabolisms is null)
                continue;

            foreach (var entry in reagent.Metabolisms.Metabolisms.Values)
            foreach (var effect in entry.Effects)
            {
                if (effect is Addictive addictive && (string) addictive.Reagent != reagent.ID)
                    throw new InvalidDataException(
                        $"Addictive on {reagent.ID} declares reagent {addictive.Reagent}");
            }
        }
    }

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<Addictive> args)
    {
        _addiction.Expose(entity, args.Effect, args.Scale);
    }
}

/// <summary>Tracks dependence through exposure, satisfaction, withdrawal, and recovery.</summary>
public sealed partial class AddictionSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SanitySystem _sanity = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

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
            state.Progress = comp.DependenceStart;
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
            _sanity.ApplySanityDelta((ent, sanity), SanitySource.Chemical, ent.Comp.RecoverySanityDelta);

        _popup.PopupEntity(Loc.GetString("oxyd-medical-addiction-recovered",
            ("reagent", _prototypes.Index(reagent).LocalizedName)), ent, ent);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AddictionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_mobs.IsDead(uid))
                continue;

            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + TimeSpan.FromSeconds(comp.UpdateInterval);
            foreach (var (reagent, state) in comp.Reagents.ToArray())
            {
                // While the reagent is still in the bloodstream, the Addictive entity effect
                // keeps firing Expose each metabolism tick and pinning Progress at -15.
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

                // OXYD: scheduled craving roll on a predicted stream so it doesn't
                // shift the global RNG sequence mid-frame.
                if (state.Progress <= 0 || !SharedRandomExtensions.PredictedProb(_timing, comp.CravingChance, GetNetEntity(uid)))
                    continue;

                _popup.PopupEntity(Loc.GetString("oxyd-medical-addiction-craving",
                    ("reagent", _prototypes.Index(reagent).LocalizedName)), uid, uid);
                if (state.Progress > comp.CravingSanityThreshold &&
                    TryComp<SanityComponent>(uid, out var sanity))
                {
                    _sanity.ApplySanityDelta((uid, sanity), SanitySource.Chemical,
                        state.Progress > comp.CravingSevereThreshold
                            ? comp.CravingSevereSanityDelta
                            : comp.CravingSanityDelta);
                }
            }
        }
    }
}
