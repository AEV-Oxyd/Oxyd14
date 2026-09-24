using System.Linq;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Reports every cruciform-bearing body the caster can see within the litany range.</summary>
public sealed partial class LitanyCruciformSenseEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (system.CollectVisibleActiveFollowers(context.User, system.GetSenseRange(context.Litany)).Count == 0)
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var visible = system.CollectVisibleActiveFollowers(context.User, system.GetSenseRange(context.Litany));
        if (visible.Count == 0)
            return false;

        foreach (var follower in visible.OrderBy(uid => uid))
        {
            var escaped = FormattedMessage.EscapeText(system.GetName(follower, context.User));
            system.DeliverSocialNotice(context.User,
                Loc.GetString("oxyd-litany-cruciform-sense-notice", ("name", escaped)));
        }

        return true;
    }
}
