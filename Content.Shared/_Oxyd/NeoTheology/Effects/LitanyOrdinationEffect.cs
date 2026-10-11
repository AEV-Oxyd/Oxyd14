using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:411-425</c>: set an existing cruciform's clearance to Clergy.
/// </summary>
public sealed partial class LitanyOrdinationEffect : LitanyEffect<LitanyOrdinationEffect>
{
    public LitanyOrdinationEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
        ];
    }
}

/// <summary>Raised once on the caster: grants Clergy clearance to the first resolved follower.</summary>
public sealed partial class LitanyOrdinationEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyOrdinationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyOrdinationEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var rank = new LitanySetClearanceEvent(target, NeoTheologyClearance.Clergy, false);
            _litany.RaiseOn(target, ref rank);
            if (rank.Handled && args.User is { } user)
                _litany.AdminLog(LogType.Action, LogImpact.Medium, user,
                    $"ordained via {_litany.GetCastContext(user)?.Litany.ID} to Clergy clearance on", target);
            handled = rank.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
