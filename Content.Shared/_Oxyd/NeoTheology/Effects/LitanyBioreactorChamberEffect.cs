using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:219-236</c> (bioreactor chamber doors): the chamber door is
/// opened or shut after re-checking what jams it. The check and the toggle are server-side, so
/// the effect raises <see cref="LitanyToggleBioreactorChamberEvent"/> on the reactor; the server
/// <c>BioreactorSystem</c> calls its own <c>TryToggleChamber</c>.
/// </summary>
public sealed partial class LitanyBioreactorChamberEffect : LitanyEffect<LitanyBioreactorChamberEffect>
{
    public LitanyBioreactorChamberEffect()
    {
        Conditions =
        [
            // The validate probe runs the same jam check the toggle does.
            new LitanyBioreactorChamberCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>
/// Cast-scoped: toggles the chamber on the first workable bioreactor among the resolved
/// machine targets.
/// </summary>
public sealed partial class LitanyBioreactorChamberEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyBioreactorChamberEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyBioreactorChamberEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        foreach (var target in ctx.Targets)
        {
            if (!_litany.IsLitanyBioreactor(target))
                continue;

            var toggle = new LitanyToggleBioreactorChamberEvent(target, false);
            _litany.RaiseOn(target, ref toggle);
            if (toggle.Handled)
            {
                _litany.ReportApplyResult(args.User, true);
                return;
            }
        }

        _litany.ReportApplyResult(args.User, false);
    }
}
