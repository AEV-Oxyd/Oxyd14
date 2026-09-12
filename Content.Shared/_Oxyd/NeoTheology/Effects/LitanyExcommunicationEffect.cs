using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:451-490</c> (Excommunication). The source does exactly two things to
/// the target's cruciform: <c>remove_specialization()</c> (drop the Acolyte/Agrolyte/Custodian
/// modules) and <c>security_clearance = CLEARANCE_NONE</c>, then tells the target they have been
/// cut off from the Church. It does not eject the implant and it applies no timed debuff.
/// The fork expresses specialization and clearance through the rank, so the plan maps this to the
/// full "return to disciple status" swap: the target is demoted to the Disciple profile (base
/// module only, Follower+Common access, no clergy sets).
/// Deferred (flagged): Eris keeps priest/inquisitor modules while only removing the
/// specialization; the rank model has no "disciple + priest modules" state, so the demotion is
/// complete. Eris' Godblood-mutation gate is not ported (no mutation model).
/// ponytail: no status-effect/debuff API exists for a "long debuff"; add it with the duration
/// when an NT status-effect model lands. Eris addresses one validated identity; this effect acts
/// on the first sorted resolved target until the identity choice UI lands.
/// </summary>
public sealed partial class LitanyExcommunicationEffect : LitanyEffect
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> DiscipleProfile = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 || !system.TryGetActiveCruciform(context.Targets[0], out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        // Eris: `if(CI.get_module(CRUCIFORM_INQUISITOR)) fail("You don't have the authority for this.")`.
        if (cruciform.Profile == InquisitorProfile)
        {
            failure = "oxyd-litany-no-authority";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];
        var rank = new LitanySetRankEvent(target, DiscipleProfile, false);
        system.RaiseOn(target, ref rank);
        if (!rank.Handled)
            return false;

        // Eris: to_chat(M, SPAN_DANGER("You have been spiritually separated...")) — delivered on
        // announcement only, exactly as the source does.
        system.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-excommunication-notice"));
        return true;
    }
}
