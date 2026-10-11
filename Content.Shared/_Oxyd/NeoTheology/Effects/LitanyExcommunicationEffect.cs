using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:451-490</c>: drop the specialization modules, set clearance to
/// None, and tell the target. Priest and inquisitor modules stay. An Inquisitor or a Godblood
/// bearer is refused. The named follower is the single target the cast layer recorded.
/// </summary>
public sealed partial class LitanyExcommunicationEffect : LitanyEffect<LitanyExcommunicationEffect>
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";

    /// <summary>The pipeline delivers each resolved bearer; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyExcommunicationEffect()
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

/// <summary>
/// Strips each resolved bearer's specialization modules, drops their clearance, and delivers
/// the separation notice.
/// </summary>
public sealed partial class LitanyExcommunicationEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyExcommunicationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyExcommunicationEffect> args)
    {
        var strip = new LitanyRemoveSpecializationEvent(entity.Owner, false);
        _litany.RaiseOn(entity.Owner, ref strip);
        if (!strip.Handled)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var clearance = new LitanySetClearanceEvent(entity.Owner, NeoTheologyClearance.None, false);
        _litany.RaiseOn(entity.Owner, ref clearance);
        if (!clearance.Handled)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Eris: to_chat(M, SPAN_DANGER("You have been spiritually separated...")) — delivered on
        // announcement only, exactly as the source does.
        if (_litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.High, args.User ?? entity.Owner,
                $"excommunicated via {ctx.Litany.ID} on", entity.Owner);
        }

        _litany.DeliverSocialNotice(entity.Owner, Loc.GetString("oxyd-litany-excommunication-notice"));
        _litany.ReportApplyResult(args.User, true);
    }
}
