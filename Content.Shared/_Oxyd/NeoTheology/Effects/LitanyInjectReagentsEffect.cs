using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityConditions;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Transfers the full holy dose into the selected patient's blood. Metabolism applies its effects.</summary>
public sealed partial class LitanyInjectReagentsEffect : LitanyEffect<LitanyInjectReagentsEffect>, ISerializationHooks
{
    /// <summary>The pipeline delivers each resolved patient; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    [DataField]
    public bool CheckBiologicalRejection;

    void ISerializationHooks.AfterDeserialization()
    {
        // Both gates read DataFields (the rejection flag, the summed dose volume), so they can
        // only be installed once values load. Rejection precedes the dose check to match the
        // Eris order. The rebuild is idempotent: round-tripped data may already carry the
        // derived gates, which are stripped before the fresh ones are built — otherwise every
        // write-read-write cycle would append another copy.
        var kept = Conditions?.Where(condition =>
                condition is not LitanyCanInjectCondition and not LitanyNotAtheistCondition)
            .ToArray() ?? [];

        var amount = Reagents.Values.Aggregate(FixedPoint2.Zero, (sum, dose) => sum + dose);
        var derived = new List<EntityCondition>();
        if (CheckBiologicalRejection)
        {
            derived.Add(new LitanyNotAtheistCondition { FailureId = "oxyd-litany-biological-rejection" });
        }

        derived.Add(new LitanyCanInjectCondition { MinVolume = amount, FailureId = "oxyd-litany-no-effect" });
        Conditions = [.. derived, .. kept];
    }
}

/// <summary>Injects the full holy dose into each resolved patient's bloodstream.</summary>
public sealed partial class LitanyInjectReagentsEntityEffectSystem
    : EntityEffectSystem<BloodstreamComponent, LitanyInjectReagentsEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<LitanyInjectReagentsEffect> args)
    {
        var effect = args.Effect;
        var amount = FixedPoint2.Zero;
        foreach (var dose in effect.Reagents.Values)
        {
            if (dose <= 0)
            {
                // Malformed litany data — Eris refused the cast for a non-positive dose.
                _litany.ReportApplyResult(args.User, false);
                return;
            }

            amount += dose;
        }

        if (amount <= 0 || !_litany.CanInjectMedicine(entity, amount))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        _litany.ReportApplyResult(args.User, _litany.InjectMedicine(entity, effect.Reagents));
    }
}
