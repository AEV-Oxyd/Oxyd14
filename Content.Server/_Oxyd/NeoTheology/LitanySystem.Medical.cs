using Content.Shared._Oxyd.NeoTheology;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// M4 Packet B: Relief (analgesia) and SoulHunger (satiation + Heat) commit handlers.
/// </summary>
public sealed partial class LitanySystem
{
    /// <summary>Eris soul_hunger nutrition delta; Oxyd routes through SatiationSystem Hunger.</summary>
    public const float SoulHungerNutritionAmount = 100f;

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SatiationSystem _satiation = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    /// <summary>
    /// Validates that the litany's effect can apply to the actor. Called before debit
    /// so a failed plan never charges holiness or starts a cooldown.
    /// </summary>
    private bool TryValidateEffect(EntityUid actor, LitanyPrototype litany, out LocId? failure)
    {
        failure = null;
        return litany.Effect switch
        {
            LitanyEffectKind.Relief => TryValidateRelief(actor, litany, out failure),
            LitanyEffectKind.SoulHunger => TryValidateSoulHunger(actor, litany, out failure),
            _ => true,
        };
    }

    /// <summary>
    /// Applies a previously validated effect. Must only be called after a successful
    /// <see cref="TryValidateEffect"/> and holiness debit.
    /// </summary>
    private bool TryApplyEffect(EntityUid actor, LitanyPrototype litany)
    {
        return litany.Effect switch
        {
            LitanyEffectKind.Relief => TryApplyRelief(actor, litany),
            LitanyEffectKind.SoulHunger => TryApplySoulHunger(actor, litany),
            _ => true,
        };
    }

    private bool TryValidateRelief(EntityUid actor, LitanyPrototype litany, out LocId? failure)
    {
        failure = null;
        if (litany.Parameters?.Healing?.Analgesia is not { } analgesia)
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        if (!_mobState.IsAlive(actor))
        {
            failure = "oxyd-litany-denied-npc";
            return false;
        }

        // OxydNtAnalgesia whitelist requires MobState + MobThresholds.
        if (!HasComp<MobStateComponent>(actor) || !HasComp<MobThresholdsComponent>(actor))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        var duration = litany.EffectDuration > TimeSpan.Zero
            ? litany.EffectDuration
            : TimeSpan.FromSeconds(60);
        if (duration <= TimeSpan.Zero)
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        if (!ProtoMan.TryIndex(analgesia, out _))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        return true;
    }

    private bool TryApplyRelief(EntityUid actor, LitanyPrototype litany)
    {
        if (litany.Parameters?.Healing?.Analgesia is not { } analgesia)
            return false;

        var duration = litany.EffectDuration > TimeSpan.Zero
            ? litany.EffectDuration
            : TimeSpan.FromSeconds(60);

        // Max(remaining, new) — refresh-not-stack / non-additive.
        return _statusEffects.TryUpdateStatusEffectDuration(actor, analgesia, duration);
    }

    private bool TryValidateSoulHunger(EntityUid actor, LitanyPrototype litany, out LocId? failure)
    {
        failure = null;

        if (!TryComp(actor, out SatiationComponent? satiation))
        {
            failure = "oxyd-litany-no-hunger";
            return false;
        }

        var satEntity = new Entity<SatiationComponent>(actor, satiation);
        if (_satiation.GetValueOrNull(satEntity, SatiationSystem.Hunger) is not { } hunger ||
            _satiation.GetMaximumValue(satEntity, SatiationSystem.Hunger) is not { } maxHunger)
        {
            failure = "oxyd-litany-no-hunger";
            return false;
        }

        // Already full: cannot receive the blessing (no thirst/food spawn fallback).
        if (hunger >= maxHunger)
        {
            failure = "oxyd-litany-no-hunger";
            return false;
        }

        if (!TryResolveSoulHungerHeat(litany, out _) || !HasComp<DamageableComponent>(actor))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        // Godmode cancels damage → paired Heat cannot apply.
        if (HasComp<GodmodeComponent>(actor))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        return true;
    }

    private bool TryApplySoulHunger(EntityUid actor, LitanyPrototype litany)
    {
        if (!TryComp(actor, out SatiationComponent? satiation))
            return false;

        var satEntity = new Entity<SatiationComponent>(actor, satiation);
        if (_satiation.GetValueOrNull(satEntity, SatiationSystem.Hunger) is not { } hungerBefore ||
            _satiation.GetMaximumValue(satEntity, SatiationSystem.Hunger) is not { } maxHunger)
            return false;

        if (hungerBefore >= maxHunger)
            return false;

        if (!TryResolveSoulHungerHeat(litany, out var heatDamage))
            return false;

        // Apply paired Heat first so a damage failure never mutates Hunger.
        if (!_damageable.TryChangeDamage(
                actor,
                heatDamage,
                ignoreResistances: true,
                interruptsDoAfters: false,
                origin: actor,
                ignoreGlobalModifiers: true))
            return false;

        _satiation.ModifyValue(satEntity, SatiationSystem.Hunger, SoulHungerNutritionAmount);
        return true;
    }

    /// <summary>
    /// Catalog stores SoulHunger Heat as a negative healing-family value; runtime deals
    /// the absolute magnitude as positive Heat damage.
    /// </summary>
    private static bool TryResolveSoulHungerHeat(LitanyPrototype litany, out DamageSpecifier heatDamage)
    {
        heatDamage = new DamageSpecifier();
        if (litany.Parameters?.Healing is null ||
            !litany.Parameters.Healing.Damage.DamageDict.TryGetValue("Heat", out var heat) ||
            heat >= FixedPoint2.Zero)
            return false;

        // Validator requires negative YAML values; invert to positive Heat damage.
        heatDamage.DamageDict["Heat"] = -heat;
        return heatDamage.AnyPositive();
    }
}
