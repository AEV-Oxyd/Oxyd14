using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Short-boost carrier: applies one timed unique buff per listed skill to every eligible
/// target resolved at begin time. <see cref="SharedSkillSystem.SetUniqueBuff"/> keys the
/// buff by (skill, source), so recasting the same chant refreshes this effect's entry
/// instead of stacking; expiry is owned by the skill system.
/// </summary>
public sealed partial class LitanySkillEffect : LitanyEffect
{
    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, int> Amounts = new();

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        foreach (var target in context.Targets)
        {
            if (system.CanReceiveSkillBuff(target))
            {
                failure = null;
                return true;
            }
        }

        failure = "oxyd-litany-no-target";
        return false;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var applied = false;
        foreach (var target in context.Targets)
        {
            applied |= system.TryApplySkillBuff(target, context.Litany.ID, Amounts, context.Litany.EffectDuration);
        }

        return applied;
    }
}
