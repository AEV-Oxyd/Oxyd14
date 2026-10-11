using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:228-256</c> (uninstall_upgrade): the attachment detaches from the
/// follower's cruciform and returns to the altar turf. Both halves are server-side, so the
/// effect raises <see cref="LitanyUninstallUpgradeEvent"/> on the target and the server
/// CruciformUpgradeSystem detaches the item and drops it at the bearer's coordinates.
/// </summary>
public sealed partial class LitanyUninstallUpgradeEffect : LitanyEffect<LitanyUninstallUpgradeEffect>
{
    /// <summary>Each resolved follower lying on an altar gets their upgrade detached.</summary>
    public override bool PerTarget => true;

    public LitanyUninstallUpgradeEffect()
    {
        Conditions =
        [
            new LitanyInstalledCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            new LitanyCruciformUpgradeInstalledCondition { FailureId = "oxyd-litany-upgrade-not-installed" },
            new LitanyProcedureAltarCondition { Undressed = false },
        ];
    }
}

/// <summary>Raises the uninstall-upgrade bridge on each resolved follower.</summary>
public sealed partial class LitanyUninstallUpgradeEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyUninstallUpgradeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyUninstallUpgradeEffect> args)
    {
        var uninstall = new LitanyUninstallUpgradeEvent(entity, false);
        _litany.RaiseOn(entity, ref uninstall);
        _litany.ReportApplyResult(args.User, uninstall.Handled);
    }
}
