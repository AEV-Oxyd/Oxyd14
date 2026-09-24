using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:411-425</c> (Ordination: raise the target's clearance to Clergy).
/// The fork expresses Clergy through the Preacher rank, whose profile carries the Clergy access
/// and the Priest litany set, so the plan maps Ordination to the full Acolyte → Preacher swap.
/// The rank swap is server-only, reached through <see cref="LitanySetRankEvent"/>.
/// </summary>
public sealed partial class LitanyOrdinationEffect : LitanyEffect
{
    private static readonly ProtoId<NeoTheologyProfilePrototype> PreacherProfile = "OxydNtPreacher";

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
        var rank = new LitanySetRankEvent(target, PreacherProfile, false);
        system.RaiseOn(target, ref rank);
        return rank.Handled;
    }
}
