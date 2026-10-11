using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:395-409</c>: set an existing cruciform's clearance to Common.
/// </summary>
public sealed partial class LitanyAdoptionEffect : LitanyEffect<LitanyAdoptionEffect>
{
    /// <summary>The pipeline delivers each resolved bearer; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyAdoptionEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
        ];
    }
}

/// <summary>Grants Common clearance on each resolved bearer's cruciform.</summary>
public sealed partial class LitanyAdoptionEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyAdoptionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyAdoptionEffect> args)
    {
        var grant = new LitanySetClearanceEvent(entity.Owner, NeoTheologyClearance.Common, false);
        _litany.RaiseOn(entity.Owner, ref grant);
        if (grant.Handled && _litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.Medium, args.User ?? entity.Owner,
                $"converted via {ctx.Litany.ID} to Common clearance on", entity.Owner);
        }

        _litany.ReportApplyResult(args.User, grant.Handled);
    }
}
