using System.Linq;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:331-359</c>: the caster must hold an oddity. For every
/// non-zero skill in its <c>giving</c> dict, roll <c>gain = rand(1..8)</c>, add it to the
/// oddity, and reduce the caster's own skill by <c>max(round(gain/2), 1)</c> — skill
/// reduction rides <see cref="Content.Shared._Oxyd.Skills.SharedSkillSystem.SetUniqueBuff"/>
/// with a negative amount and the litany's ID as the unique key.
/// </summary>
public sealed partial class LitanyDivineBlessingEffect : LitanyEffect<LitanyDivineBlessingEffect>
{
    /// <summary>Eris <c>rand(1,8)</c> oddity gain bounds.</summary>
    public const int MinGain = 1;
    public const int MaxGain = 8;

    public LitanyDivineBlessingEffect()
    {
        Conditions =
        [
            // Held, unblessed, giving-capable oddity — reports the Eris failure lines.
            new LitanyBlessableOddityCondition(),
        ];
    }
}

/// <summary>Cast-scoped: blesses the caster's held oddity and debits the caster's skills.</summary>
public sealed partial class LitanyDivineBlessingEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyDivineBlessingEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyDivineBlessingEffect> args)
    {
        var sourceId = _litany.GetCastContext(args.User)?.Litany.ID ?? string.Empty;
        if (!_litany.TryGetHeldOddity(entity.Owner, out var item, out var oddity) ||
            _litany.IsOddityBlessed(item))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var blessed = false;
        foreach (var (skill, value) in oddity.giving.ToArray())
        {
            if (value == 0)
                continue;

            var gain = _litany.RollInclusive(LitanyDivineBlessingEffect.MinGain, LitanyDivineBlessingEffect.MaxGain);
            oddity.giving[skill] = value + gain;

            // The blessing penalty rides the litany's unique key: recasting refreshes
            // it instead of stacking without bound.
            _litany.TryAddTimedSkill(entity.Owner, sourceId, skill, -Math.Max((gain + 1) / 2, 1));
            blessed = true;
        }

        if (blessed)
            _litany.MarkOddityBlessed(item);

        _litany.ReportApplyResult(args.User, blessed);
    }
}
