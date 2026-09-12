using Content.Shared.Damage;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Adds <see cref="LitanyEffectSystem.SoulHungerNutritionAmount"/> hunger and deals the
/// configured injury. Eris stores the paired Heat damage as an injury, so
/// <see cref="Damage"/> holds positive values.
/// </summary>
public sealed partial class LitanySoulHungerEffect : LitanyEffect
{
    [DataField]
    public DamageSpecifier Damage = new();

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!system.TryGetHunger(context.User, out _, out var hunger, out var maxHunger) ||
            hunger >= maxHunger)
        {
            failure = "oxyd-litany-no-hunger";
            return false;
        }

        if (Damage.Empty || !system.CanReceiveDamage(context.User))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (!system.TryGetHunger(context.User, out var satiation, out var hunger, out var maxHunger) ||
            hunger >= maxHunger)
            return false;

        // Apply the paired injury first so a damage failure never mutates Hunger.
        if (!system.TryApplyDamage(context.User, Damage))
            return false;

        system.AddHunger(satiation, LitanyEffectSystem.SoulHungerNutritionAmount);
        return true;
    }
}
