using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:200-213</c> (bioreactor solution): the closed chamber is pumped
/// in or out. The pump is server-side, so the effect raises
/// <see cref="LitanyPumpBioreactorEvent"/> on the reactor; the server <c>BioreactorSystem</c>
/// calls its own <c>TryPumpSolution</c>.
/// </summary>
public sealed partial class LitanyBioreactorSolutionEffect : LitanyEffect<LitanyBioreactorSolutionEffect>
{
    public LitanyBioreactorSolutionEffect()
    {
        Conditions =
        [
            // The validate probe runs the same pump check the apply path does.
            new LitanyBioreactorPumpCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>
/// Cast-scoped: pumps the first workable bioreactor among the resolved machine targets.
/// </summary>
public sealed partial class LitanyBioreactorSolutionEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyBioreactorSolutionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyBioreactorSolutionEffect> args)
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

            var pump = new LitanyPumpBioreactorEvent(target, false);
            _litany.RaiseOn(target, ref pump);
            if (pump.Handled)
            {
                _litany.ReportApplyResult(args.User, true);
                return;
            }
        }

        _litany.ReportApplyResult(args.User, false);
    }
}
