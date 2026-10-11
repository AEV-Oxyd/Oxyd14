using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:411-425</c>: set an existing cruciform's clearance to Clergy.
/// </summary>
public sealed partial class LitanyOrdinationEffect : LitanyEffect<LitanyOrdinationEffect>
{
    /// <summary>Each resolved follower gets their clearance set to Clergy.</summary>
    public override bool PerTarget => true;

    public LitanyOrdinationEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
        ];
    }
}

/// <summary>Raises the clearance bridge on each resolved follower.</summary>
public sealed partial class LitanyOrdinationEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyOrdinationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyOrdinationEffect> args)
    {
        var rank = new LitanySetClearanceEvent(entity, NeoTheologyClearance.Clergy, false);
        _litany.RaiseOn(entity, ref rank);
        if (rank.Handled && args.User is { } user)
            _litany.AdminLog(LogType.Action, LogImpact.Medium, user,
                $"ordained via {_litany.GetCastContext(user)?.Litany.ID} to Clergy clearance on", entity);
        _litany.ReportApplyResult(args.User, rank.Handled);
    }
}
