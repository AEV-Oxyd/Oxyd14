using Content.Shared._Oxyd.Skills;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Short-boost carrier: applies one timed unique buff per listed skill to every eligible
/// target resolved at begin time. <see cref="SharedSkillSystem.SetUniqueBuff"/> keys the
/// buff by (skill, source), so recasting the same chant refreshes this effect's entry
/// instead of stacking; expiry is owned by the skill system.
/// </summary>
public sealed partial class LitanySkillEffect : LitanyEffect<LitanySkillEffect>
{
    /// <summary>Each resolved member eligible for the buff gets it independently.</summary>
    public override bool PerTarget => true;

    public LitanySkillEffect()
    {
        Conditions =
        [
            new LitanySkillBuffRecipientCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }

    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, int> Amounts = new();
}

/// <summary>Applies or refreshes the litany-keyed buffs on each resolved target.</summary>
public sealed partial class LitanySkillEntityEffectSystem
    : EntityEffectSystem<MobSkillComponent, LitanySkillEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<MobSkillComponent> entity, ref EntityEffectEvent<LitanySkillEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } context)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var applied = _litany.TryApplySkillBuff(
            entity, context.Litany.ID, args.Effect.Amounts, context.Litany.EffectDuration);
        _litany.ReportApplyResult(args.User, applied);
    }
}
