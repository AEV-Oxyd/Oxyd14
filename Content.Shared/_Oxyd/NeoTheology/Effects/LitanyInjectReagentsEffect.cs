using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Transfers the full holy dose into the selected patient's blood. Metabolism applies its effects.</summary>
public sealed partial class LitanyInjectReagentsEffect : LitanyEffect
{
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    [DataField]
    public bool CheckBiologicalRejection;

    public override bool CanApply(LitanyEffectSystem system, LitanyEffectContext context, out LocId? failure)
    {
        var target = system.MedicalTarget(context);
        if (CheckBiologicalRejection && system.RejectsHolyInfluence(target))
        {
            failure = "oxyd-litany-biological-rejection";
            return false;
        }

        var amount = FixedPoint2.Zero;
        foreach (var dose in Reagents.Values)
        {
            if (dose <= 0)
            {
                failure = "oxyd-litany-no-effect";
                return false;
            }
            amount += dose;
        }

        if (amount <= 0 || !system.CanInjectMedicine(target, amount))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context) =>
        CanApply(system, context, out _) && system.InjectMedicine(system.MedicalTarget(context), Reagents);
}
