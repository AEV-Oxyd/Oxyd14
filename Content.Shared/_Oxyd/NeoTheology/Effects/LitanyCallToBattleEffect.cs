using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Call to Battle (Eris <c>rituals/crusader.dm:24-51</c>): the caster gains Toughness and
/// Robustness equal to twice the number of cruciform bearers in view (the caster included) and
/// half that much Vigilance, for ten minutes. Eris counts every human with an installed implant;
/// the fork counts active bearers through the shared follower scan.
/// </summary>
public sealed partial class LitanyCallToBattleEffect : LitanyEffect
{
    /// <summary>Eris <c>count += 2</c> per bearer, the caster included.</summary>
    public const int PointsPerBearer = 2;

    /// <summary>Eris counts every bearer in view; the fork scans this radius.</summary>
    [DataField]
    public float Radius = 7f;

    private static readonly ProtoId<SkillPrototype> Toughness = "Tgh";
    private static readonly ProtoId<SkillPrototype> Robustness = "Rob";
    private static readonly ProtoId<SkillPrototype> Vigilance = "Vig";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!system.CanReceiveSkillBuff(context.User))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        // The caster bears an active cruciform to cast at all, so they count first.
        var count = PointsPerBearer;
        foreach (var _ in system.CollectVisibleActiveFollowers(context.User, Radius))
            count += PointsPerBearer;

        var amounts = new Dictionary<ProtoId<SkillPrototype>, int>
        {
            [Toughness] = count,
            [Robustness] = count,
            [Vigilance] = count / 2,
        };

        return system.TryApplySkillBuff(context.User, context.Litany.ID, amounts, context.Litany.EffectDuration);
    }
}
