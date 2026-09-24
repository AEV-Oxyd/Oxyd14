using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:395-409</c> (Adoption: set the target's clearance to Common and
/// reuse the existing cruciform) mapped onto the fork's rank model, which has no separate
/// clearance field: the plan makes Adoption "baptize a non-believer" by granting a fresh,
/// active <c>OxydNtDisciple</c> cruciform — the profile whose access is exactly Follower+Common.
/// Granting is server-only, so the effect raises <see cref="LitanyGrantCruciformEvent"/> on the
/// target and the server CruciformSystem does the work.
/// </summary>
public sealed partial class LitanyAdoptionEffect : LitanyEffect
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> DiscipleProfile = "OxydNtDisciple";

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

        // Already a bearer: Eris only resets clearance on the existing implant, but the grant
        // path here would double-implant. Recast is refused instead of stacking a second one.
        if (system.TryGetInstalledCruciform(target, out _))
        {
            failure = "oxyd-litany-commitment-has-cruciform";
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
        var grant = new LitanyGrantCruciformEvent(target, DiscipleProfile, false);
        system.RaiseOn(target, ref grant);
        return grant.Handled;
    }
}
