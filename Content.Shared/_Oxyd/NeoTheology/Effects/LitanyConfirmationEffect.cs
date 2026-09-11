using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:361-393</c> (Confirmation: Disciple → Acolyte).
/// Flagged divergence: Eris pops an <c>alert()</c> offering Acolyte / Agrolyte / Custodian and
/// calls the matching <c>make_*()</c>; the plan fixes the designation to Acolyte, so this port
/// implements the Acolyte-only version and invents no choice UI. The rank swap is server-only,
/// reached through <see cref="LitanySetRankEvent"/>.
/// </summary>
public sealed partial class LitanyConfirmationEffect : LitanyEffect
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> AcolyteProfile = "OxydNtAcolyte";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 || !system.TryGetActiveCruciform(context.Targets[0], out _))
        {
            failure = "oxyd-litany-no-cruciform";
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
