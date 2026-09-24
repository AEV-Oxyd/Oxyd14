using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/custodian.dm:7-40</c> (Words of Purging): eases the target's addiction.
/// Advances dependence recovery by 15 without removing blood reagents. Also grants 15 pain relief.
/// </summary>
public sealed partial class LitanyWordsOfPurgingEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = "oxyd-litany-no-target";
        if (context.Targets.Count == 0)
            return false;

        if (context.Targets.Any(system.RejectsHolyInfluence))
        {
            failure = "oxyd-litany-biological-rejection";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (!CanApply(system, context, out _))
            return false;

        var handled = false;
        foreach (var target in context.Targets)
        {
            var purge = new LitanyPurgeAddictionEvent(target, false);
            system.RaiseOn(target, ref purge);
            handled |= purge.Handled;
        }

        return handled;
    }
}
