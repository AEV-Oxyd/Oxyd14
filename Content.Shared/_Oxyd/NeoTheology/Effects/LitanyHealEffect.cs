using Content.Shared.Damage;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Applies a negative <see cref="DamageSpecifier"/> to heal the caster.</summary>
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
        return system.TryApplyDamage(context.User, Damage);
    }
}
