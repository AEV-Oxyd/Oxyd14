using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:427-449</c> (Omission: strip every clearance from the target's
/// cruciform; refuse an Inquisitor target). The fork expresses clearance through the rank, so
/// the plan maps Omission to the Preacher → Acolyte demotion; a target below Preacher lands on
/// the same Acolyte rank.
/// Deferred (flagged): Eris also refuses a Godblood-mutated target — the fork has no mutation
/// model, so only the Inquisitor gate is ported.
/// ponytail: add the mutation gate when a mutation/status API exists; until then an Inquisitor
/// target is the only refusal, matching the source's authority gate.
/// </summary>
public sealed partial class LitanyOmissionEffect : LitanyEffect
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> AcolyteProfile = "OxydNtAcolyte";
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
        var rank = new LitanySetRankEvent(target, AcolyteProfile, false);
        system.RaiseOn(target, ref rank);
        return rank.Handled;
    }
}
