using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:400-430</c> (ejection): a dead bearer's cruciform is ripped out —
/// 15 Blunt into the corpse first, then the same implant entity is dropped on its tile. The
/// cruciform is never deleted, so it can be re-installed and scanned for Resurrection.
/// </summary>
public sealed partial class LitanyDeprivationEffect : LitanyEffect
{
    /// <summary>Eris ejection: 15 Brute to the chest before the implant comes out.</summary>
    public const int EjectDamage = 15;

    /// <summary>§7.1: the dead-only extraction flow needs Dead candidates resolved.</summary>
    public override bool AllowsDeadTarget => true;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0)
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        var target = context.Targets[0];

        if (!system.IsDead(target))
        {
            // Eris: "Deprivation does not work upon the living."
            failure = "oxyd-litany-deprivation-alive";
            return false;
        }

        if (!system.TryGetInstalledCruciformEntity(target, out _, out _))
        {
            failure = "oxyd-litany-no-cruciform";
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
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];

        // Re-read the installed cruciform at apply time; the same entity is what comes out.
        if (!system.TryGetInstalledCruciformEntity(target, out var cruciform, out _))
            return false;

        // Eris order: the 15 Blunt lands first; abort before any mutation if it cannot.
        if (!system.TryApplyDamage(
                target,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(EjectDamage) } }))
            return false;

        return system.TryExtractInstalledCruciform(target, cruciform);
    }
}
