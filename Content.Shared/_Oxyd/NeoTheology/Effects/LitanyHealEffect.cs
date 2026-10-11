using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Applies a negative <see cref="DamageSpecifier"/> to the resolved patient. Entries naming a
/// damage type ("Blunt") apply directly; entries naming a damage GROUP ("Brute") spread
/// their budget over the group's present damage instead of healing each subtype fully.
/// </summary>
public sealed partial class LitanyHealEffect : LitanyEffect<LitanyHealEffect>
{
    /// <summary>The pipeline delivers each resolved patient; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyHealEffect()
    {
        Conditions =
        [
            new LitanyAliveCondition { FailureId = "oxyd-litany-denied-npc" },
            new LitanyCanReceiveDamageCondition { FailureId = "oxyd-litany-no-effect" },
        ];
    }

    /// <summary>Negative values heal; the catalog validator rejects non-negative entries.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public float PainkillerStrength;

    /// <summary>
    /// The analgesic status effect this litany applies — one proto per litany so overlapping
    /// rites keep their own durations instead of refreshing one shared dose.
    /// </summary>
    [DataField]
    public EntProtoId? AnalgesicEffect;
}

/// <summary>Applies <see cref="LitanyHealEffect"/> to each resolved patient.</summary>
public sealed partial class LitanyHealEntityEffectSystem : EntityEffectSystem<DamageableComponent, LitanyHealEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<DamageableComponent> entity, ref EntityEffectEvent<LitanyHealEffect> args)
    {
        var effect = args.Effect;
        if (effect.Damage.Empty)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (effect.PainkillerStrength > 0 && effect.AnalgesicEffect is { } analgesic)
            _litany.RelievePain(entity, analgesic, effect.PainkillerStrength);

        foreach (var (type, value) in effect.Damage.DamageDict)
        {
            if (value >= FixedPoint2.Zero)
                continue;

            if (_litany.IsDamageGroup(type))
            {
                _litany.TryHealDamageGroup(entity, type, value);
                continue;
            }

            _litany.TryApplyDamage(entity, new DamageSpecifier { DamageDict = { [type] = value } });
        }

        // Eris permits healing an uninjured patient.
        _litany.ReportApplyResult(args.User, true);
    }
}
