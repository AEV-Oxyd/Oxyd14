using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:177-226</c> (install_upgrade): the upgrade item resting on the altar
/// beside the follower is attached to their cruciform. The altar lookup and the attach are both
/// server-side, so the effect raises <see cref="LitanyInstallUpgradeEvent"/> on the target and
/// the server CruciformUpgradeSystem does the work.
/// The target must occupy the altar, lie down, and remove worn clothing.
/// </summary>
public sealed partial class LitanyInstallUpgradeEffect : LitanyEffect<LitanyInstallUpgradeEffect>
{
    /// <summary>Each resolved follower lying on an altar gets the upgrade beside them installed.</summary>
    public override bool PerTarget => true;

    public LitanyInstallUpgradeEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            new LitanyCruciformUpgradeFreeCondition { FailureId = "oxyd-litany-upgrade-present" },
            new LitanyProcedureAltarCondition { Undressed = true },
            new LitanyAltarUpgradePresentCondition { FailureId = "oxyd-litany-upgrade-missing" },
        ];
    }
}

/// <summary>Raises the install-upgrade bridge on each resolved follower.</summary>
public sealed partial class LitanyInstallUpgradeEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyInstallUpgradeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyInstallUpgradeEffect> args)
    {
        var install = new LitanyInstallUpgradeEvent(entity, false);
        _litany.RaiseOn(entity, ref install);
        _litany.ReportApplyResult(args.User, install.Handled);
    }
}
