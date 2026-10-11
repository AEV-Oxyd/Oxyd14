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
    public LitanyInstallUpgradeEffect()
    {
        Conditions =
        [
            new LitanyFirstTargetActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            new LitanyCruciformUpgradeFreeCondition { FailureId = "oxyd-litany-upgrade-present" },
            new LitanyFirstTargetProcedureAltarCondition { Undressed = true },
            new LitanyAltarUpgradePresentCondition { FailureId = "oxyd-litany-upgrade-missing" },
        ];
    }
}

/// <summary>Raised once on the caster: installs the altar upgrade on the first resolved follower.</summary>
public sealed partial class LitanyInstallUpgradeEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyInstallUpgradeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyInstallUpgradeEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var install = new LitanyInstallUpgradeEvent(target, false);
            _litany.RaiseOn(target, ref install);
            handled = install.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
