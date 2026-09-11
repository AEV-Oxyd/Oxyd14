using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:177-226</c> (install_upgrade): the upgrade item resting on the altar
/// beside the follower is attached to their cruciform. The altar lookup and the attach are both
/// server-side, so the effect raises <see cref="LitanyInstallUpgradeEvent"/> on the target and
/// the server CruciformUpgradeSystem does the work.
/// Deferred (same simplification as Commitment): Eris also requires the follower to be lying on
/// the altar, undressed, with the altar seat occupied — the fork has no altar strap and no
/// inventory gate, so "an altar is within reach of the target" stands in for the lying check.
/// </summary>
public sealed partial class LitanyInstallUpgradeEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 || !system.TryGetActiveCruciform(context.Targets[0], out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        if (cruciform.Upgrade is not null)
        {
            failure = "oxyd-litany-upgrade-present";
            return false;
        }

        if (!system.TryFindAltarUpgrade(context.Targets[0], out _, out _))
        {
            failure = "oxyd-litany-upgrade-missing";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var install = new LitanyInstallUpgradeEvent(context.Targets[0], false);
        system.RaiseOn(context.Targets[0], ref install);
        return install.Handled;
    }
}
