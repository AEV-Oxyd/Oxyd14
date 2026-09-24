using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Applies a negative <see cref="DamageSpecifier"/> to heal the selected patient. Entries naming a
/// damage type ("Blunt") apply directly; entries naming a damage GROUP ("Brute") spread
/// their budget over the group's present damage instead of healing each subtype fully.
/// </summary>
public sealed partial class LitanyHealEffect : LitanyEffect
{
    /// <summary>Negative values heal; the catalog validator rejects non-negative entries.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public float PainkillerStrength;

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

        var target = system.MedicalTarget(context);
        if (target == EntityUid.Invalid || system.IsDead(target))
        {
            failure = "oxyd-litany-denied-npc";
            return false;
        }

        if (!system.CanReceiveDamage(target))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (!CanApply(system, context, out _))
            return false;

        var target = system.MedicalTarget(context);
        system.RelievePain(target, context.Litany.ID, PainkillerStrength);
        foreach (var (type, value) in Damage.DamageDict)
        {
            if (value >= FixedPoint2.Zero)
                continue;

            if (system.IsDamageGroup(type))
            {
                system.TryHealDamageGroup(target, type, value);
                continue;
            }

            system.TryApplyDamage(target, new DamageSpecifier { DamageDict = { [type] = value } });
        }

        // Eris permits healing an uninjured patient.
        return true;
    }
}
