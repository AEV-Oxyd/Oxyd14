using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Applies a negative <see cref="DamageSpecifier"/> to heal the caster. Entries naming a
/// damage type ("Blunt") apply directly; entries naming a damage GROUP ("Brute") spread
/// their budget over the group's present damage instead of healing each subtype fully.
/// </summary>
public sealed partial class LitanyHealEffect : LitanyEffect
{
    /// <summary>Negative values heal; the catalog validator rejects non-negative entries.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (Damage.Empty)
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        if (!system.IsAlive(context.User))
        {
            failure = "oxyd-litany-denied-npc";
            return false;
        }

        if (!system.CanReceiveDamage(context.User))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        // Negative catalog values heal. Casting at full health is a no-op but still
        // spends holiness, matching Eris relief always applying.
        var applied = false;
        foreach (var (type, value) in Damage.DamageDict)
        {
            if (value >= FixedPoint2.Zero)
                continue;

            if (system.IsDamageGroup(type))
            {
                applied |= system.TryHealDamageGroup(context.User, type, value);
                continue;
            }

            applied |= system.TryApplyDamage(context.User, new DamageSpecifier { DamageDict = { [type] = value } });
        }

        return applied;
    }
}
