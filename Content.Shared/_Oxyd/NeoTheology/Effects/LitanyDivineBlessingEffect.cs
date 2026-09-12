using System.Linq;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:331-359</c>: the caster must hold an oddity. For every
/// non-zero skill in its <c>giving</c> dict, roll <c>gain = rand(1..8)</c>, add it to the
/// oddity, and reduce the caster's own skill by <c>max(round(gain/2), 1)</c> — skill
/// reduction rides <see cref="Content.Shared._Oxyd.Skills.SharedSkillSystem.SetUniqueBuff"/>
/// with a negative amount and the litany's ID as the unique key.
/// </summary>
public sealed partial class LitanyDivineBlessingEffect : LitanyEffect
{
    /// <summary>Eris <c>rand(1,8)</c> oddity gain bounds.</summary>
    public const int MinGain = 1;
    public const int MaxGain = 8;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!system.TryGetHeldOddity(context.User, out var oddity))
        {
            failure = "oxyd-litany-no-oddity";
            return false;
        }

        if (!oddity.giving.Values.Any(value => value != 0))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (!system.TryGetHeldOddity(context.User, out var oddity))
            return false;

        // ponytail: Eris keeps a round-scoped `odditys` no-re-bless list; the caster's own
        // skill pays for every roll, which already caps re-blessing, so the bookkeeping is skipped.
        var blessed = false;
        foreach (var (skill, value) in oddity.giving.ToArray())
        {
            if (value == 0)
                continue;

            var gain = system.RollInclusive(MinGain, MaxGain);
            oddity.giving[skill] = value + gain;

            // Eris changeStat(stat, -max(round(stat_gain/2), 1)); DM round() is half-up.
            system.TryApplySkillPenalty(context.User, context.Litany.ID, skill, -Math.Max((gain + 1) / 2, 1));
            blessed = true;
        }

        return blessed;
    }
}
