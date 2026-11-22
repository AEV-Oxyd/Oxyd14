using System.Linq;
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
    public LitanyUninstallUpgradeEffect()
    {
        Conditions =
        [
            new LitanyFirstTargetInstalledCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            new LitanyCruciformUpgradeInstalledCondition { FailureId = "oxyd-litany-upgrade-not-installed" },
            new LitanyFirstTargetProcedureAltarCondition { Undressed = false },
        ];
    }
}

/// <summary>Raised once on the caster: detaches the first resolved follower's upgrade.</summary>
public sealed partial class LitanyUninstallUpgradeEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyUninstallUpgradeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyUninstallUpgradeEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var uninstall = new LitanyUninstallUpgradeEvent(target, false);
            _litany.RaiseOn(target, ref uninstall);
            handled = uninstall.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
