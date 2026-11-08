using System.Linq;
using Content.Shared.EntityEffects;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Cost-0 station-wide call; followers hear the caster's location (Eris always succeeds).</summary>
public sealed partial class LitanyEntreatyEffect : LitanyEffect<LitanyEntreatyEffect>;

/// <summary>
/// Cast-scoped: whispers the caster's name and location to every same-station active
/// follower — always to clergy, at Eris's 50% odds to the rest.
/// </summary>
public sealed partial class LitanyEntreatyEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyEntreatyEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyEntreatyEffect> args)
    {
        var escapedName = FormattedMessage.EscapeText(_litany.GetName(entity.Owner));
        var escapedLocation = FormattedMessage.EscapeText(_litany.DescribeLocation(entity.Owner));

        var recipients = _litany.EnumerateSameStationActiveFollowers(entity.Owner)
            .OrderBy(uid => uid)
            .ToList();

        foreach (var recipient in recipients)
        {
            if (!_litany.TryGetActiveCruciform(recipient, out var cruciform))
                continue;

            var always = _litany.IsClergyProfile(cruciform.Profile);
            if (!always && !_litany.Prob(0.5f))
                continue;

            _litany.DeliverSocialNotice(recipient, Loc.GetString(
                "oxyd-litany-entreaty-notice",
                ("name", escapedName),
                ("location", escapedLocation)));
        }

        _litany.ReportApplyResult(args.User, true);
    }
}
