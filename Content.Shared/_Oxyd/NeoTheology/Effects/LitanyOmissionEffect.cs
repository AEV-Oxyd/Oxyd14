using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:427-449</c>: set clearance to None. Rank modules stay.
/// An Inquisitor or a Godblood bearer is refused.
/// </summary>
public sealed partial class LitanyOmissionEffect : LitanyEffect<LitanyOmissionEffect>
{

    public LitanyOmissionEffect()
    {
        Conditions =
        [
            new LitanyFirstTargetActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            // Eris: `if(CI.get_module(CRUCIFORM_INQUISITOR)) fail("You don't have the authority for this.")`.
            new LitanyFirstTargetNotProfileCondition { Profile = NeoTheologyPrototypes.InquisitorProfile, FailureId = "oxyd-litany-no-authority" },
            new LitanyFirstTargetNoGodbloodCondition { FailureId = "oxyd-litany-godblood" },
        ];
    }
}

/// <summary>Raised once on the caster: clears the first resolved follower's clearance.</summary>
public sealed partial class LitanyOmissionEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyOmissionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyOmissionEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var clearance = new LitanySetClearanceEvent(target, NeoTheologyClearance.None, false);
            _litany.RaiseOn(target, ref clearance);
            if (clearance.Handled && args.User is { } user)
                _litany.AdminLog(LogType.Action, LogImpact.Medium, user,
                    $"omitted clearance via {_litany.GetCastContext(user)?.Litany.ID} on", target);
            handled = clearance.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
