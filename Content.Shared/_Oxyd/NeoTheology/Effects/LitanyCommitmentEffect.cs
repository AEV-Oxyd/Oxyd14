using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:311-352</c> (install): attach the loose empty cruciform resting on
/// the altar beside the target, then drive 25 Blunt into their chest in a single operation.
/// The implant lands inert — Epiphany is what activates it (§5.2).
/// </summary>
public sealed partial class LitanyCommitmentEffect : LitanyEffect
{
    /// <summary>Eris deals five 5-Brute chest hits; ported as one 25-Blunt operation.</summary>
    public const int InstallDamage = 25;

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

        // Eris install gate order: eligible victim, no existing cruciform, loose cruciform in
        // reach, living, empty implant. Non-humans are rejected before anything is consumed.
        if (!system.IsEligibleHuman(target))
        {
            failure = "oxyd-litany-not-human";
            return false;
        }

        if (!system.IsAlive(target))
        {
            failure = "oxyd-litany-commitment-too-late";
            return false;
        }

        if (system.TryGetInstalledCruciform(target, out _))
        {
            failure = "oxyd-litany-commitment-has-cruciform";
            return false;
        }

        if (!system.CanReceiveDamage(target))
        {
            failure = "oxyd-litany-no-effect";
            return false;
        }

        if (!system.TryFindAltarCruciform(target, out _, out _))
        {
            failure = "oxyd-litany-commitment-no-cruciform";
            return false;
        }

        // Deferred (§7.1): the target must be buckled to the selected altar and its jumpsuit,
        // outer clothing, head, mask, gloves and shoes slots must be empty. The altar entity
        // has no StrapComponent today and the inventory check is not modelled.
        // ponytail: add the buckle + clothing gates when the altar gains a strap seat and the
        // inventory hook exists; until then any adjacent live human at an altar is eligible.
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];

        // Commit re-runs the deterministic altar lookup: if the altar's loose cruciform moved
        // or was consumed mid-chant, the cast fails instead of grabbing a different item.
        if (!system.TryFindAltarCruciform(target, out _, out var cruciform))
            return false;

        if (!system.TryImplantLooseCruciform(target, cruciform))
            return false;

        // Eris damages the chest only after the implant is in.
        return system.TryApplyDamage(
            target,
            new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(InstallDamage) } });
    }
}
