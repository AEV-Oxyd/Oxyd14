using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Adds <see cref="LitanyEffectSystem.SoulHungerNutritionAmount"/> hunger and deals the
/// configured injury. Eris stores the paired Heat damage as an injury, so
/// <see cref="Damage"/> holds positive values.
/// </summary>
public sealed partial class LitanySoulHungerEffect : LitanyEffect<LitanySoulHungerEffect>
{
    /// <summary>The self rite resolves the caster; each resolved body takes hunger and injury.</summary>
    public override bool PerTarget => true;

    public LitanySoulHungerEffect()
    {
        Conditions =
        [
            new LitanyHungerNotFullCondition { FailureId = "oxyd-litany-no-hunger" },
            new LitanyCanReceiveDamageCondition { FailureId = "oxyd-litany-no-effect" },
        ];
    }

    [DataField]
    public DamageSpecifier Damage = new();
}

/// <summary>Applies the paired injury and the hunger fill on each resolved body.</summary>
public sealed partial class LitanySoulHungerEntityEffectSystem
    : EntityEffectSystem<DamageableComponent, LitanySoulHungerEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<DamageableComponent> entity, ref EntityEffectEvent<LitanySoulHungerEffect> args)
    {
        if (!_litany.TryGetHunger(entity, out var satiation, out var hunger, out var maxHunger) ||
            hunger >= maxHunger)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Apply the paired injury first so a damage failure never mutates Hunger.
        if (!_litany.TryApplyDamage(entity, args.Effect.Damage))
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        _litany.AddHunger(satiation, LitanyEffectSystem.SoulHungerNutritionAmount);
        _litany.ReportApplyResult(args.User, true);
    }
}
