using Content.Shared._Oxyd.NeoTheology.Components;
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
    private static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";

    /// <summary>Each resolved follower gets their clearance set to None.</summary>
    public override bool PerTarget => true;

    public LitanyOmissionEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            // Eris: `if(CI.get_module(CRUCIFORM_INQUISITOR)) fail("You don't have the authority for this.")`.
            new LitanyNotProfileCondition { Profile = InquisitorProfile, FailureId = "oxyd-litany-no-authority" },
            new LitanyNoGodbloodCondition { FailureId = "oxyd-litany-godblood" },
        ];
    }
}

/// <summary>Raises the clearance bridge on each resolved follower.</summary>
public sealed partial class LitanyOmissionEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyOmissionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyOmissionEffect> args)
    {
        var clearance = new LitanySetClearanceEvent(entity, NeoTheologyClearance.None, false);
        _litany.RaiseOn(entity, ref clearance);
        if (clearance.Handled && args.User is { } user)
            _litany.AdminLog(LogType.Action, LogImpact.Medium, user,
                $"omitted clearance via {_litany.GetCastContext(user)?.Litany.ID} on", entity);
        _litany.ReportApplyResult(args.User, clearance.Handled);
    }
}
