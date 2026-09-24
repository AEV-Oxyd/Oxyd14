using System.Linq;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Cost-0 station-wide call; followers hear the caster's location (Eris always succeeds).</summary>
public sealed partial class LitanyEntreatyEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // Cost-0 information broadcast: succeed even with zero other followers.
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var escapedName = FormattedMessage.EscapeText(system.GetName(context.User));
        var escapedLocation = FormattedMessage.EscapeText(system.DescribeLocation(context.User));

        var recipients = system.EnumerateSameStationActiveFollowers(context.User)
            .OrderBy(uid => uid)
            .ToList();

        foreach (var recipient in recipients)
        {
            if (!system.TryGetActiveCruciform(recipient, out var cruciform))
                continue;

            var always = LitanyEffectSystem.IsClergyProfile(cruciform.Profile);
            if (!always && !system.Prob(0.5f))
                continue;

            system.DeliverSocialNotice(recipient, Loc.GetString(
                "oxyd-litany-entreaty-notice",
                ("name", escapedName),
                ("location", escapedLocation)));
        }

        return true;
    }
}
