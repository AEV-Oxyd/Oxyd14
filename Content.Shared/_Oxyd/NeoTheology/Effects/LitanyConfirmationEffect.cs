using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:361-393</c> (Confirmation: Disciple → Acolyte / Agrolyte /
/// Custodian). Eris pops an <c>alert()</c> offering the three designations and calls the
/// matching <c>make_*()</c>. The book UI now offers the same three profile choices;
/// <see cref="LitanyEffectContext.Designation"/> carries the pick. Manual-speech casts
/// have no picker and keep the historical Acolyte fallback. The rank swap is server-only,
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
        var profile = context.Designation ?? AcolyteProfile;
        var rank = new LitanySetRankEvent(target, profile, false);
        system.RaiseOn(target, ref rank);
        return rank.Handled;
    }
}
