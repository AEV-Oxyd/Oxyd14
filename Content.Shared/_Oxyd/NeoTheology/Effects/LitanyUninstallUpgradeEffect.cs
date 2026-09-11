using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:228-256</c> (uninstall_upgrade): the attachment detaches from the
/// follower's cruciform and returns to the altar turf. Both halves are server-side, so the
/// effect raises <see cref="LitanyUninstallUpgradeEvent"/> on the target and the server
/// CruciformUpgradeSystem detaches the item and drops it at the bearer's coordinates.
/// </summary>
public sealed partial class LitanyUninstallUpgradeEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 || !system.TryGetInstalledCruciform(context.Targets[0], out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        if (cruciform.Upgrade is null)
        {
            failure = "oxyd-litany-upgrade-not-installed";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var uninstall = new LitanyUninstallUpgradeEvent(context.Targets[0], false);
        system.RaiseOn(context.Targets[0], ref uninstall);
        return uninstall.Handled;
    }
}
