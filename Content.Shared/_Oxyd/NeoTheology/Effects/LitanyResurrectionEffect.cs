using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:13-42</c> (resurrection): the cloner starts a soul-safe job for
/// the soul in the reader. The server checks the saved profile, mind, machines, and biomass before payment.
/// The effect raises <see cref="LitanyResurrectionEvent"/> on the cloner for validation and execution.
/// </summary>
public sealed partial class LitanyResurrectionEffect : LitanyEffect<LitanyResurrectionEffect>
{
    public LitanyResurrectionEffect()
    {
        Conditions =
        [
            new LitanyResurrectionReadyCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: the first cloner/reader pair starts the job.</summary>
public sealed partial class LitanyResurrectionEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyResurrectionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyResurrectionEffect> args)
    {
        var handled = false;
        if (args.User is { } user && _litany.GetCastContext(user) is { } context)
        {
            // Eris utters the prayer over the cloner in front. The reader is the one linked
            // to that cloner, or the nearest reader beside it.
            foreach (var target in context.Targets)
            {
                if (!_litany.IsLitanyCloner(target) ||
                    !_litany.TryResolveResurrectionReader(target, context.Targets, out var reader))
                    continue;

                var start = new LitanyResurrectionEvent(target, reader, false, false, user);
                _litany.RaiseOn(target, ref start);
                if (start.Handled)
                {
                    _litany.AdminLog(LogType.Action, LogImpact.High, user,
                        $"started resurrection via {context.Litany.ID} on", target);
                    handled = true;
                    break;
                }
            }
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
