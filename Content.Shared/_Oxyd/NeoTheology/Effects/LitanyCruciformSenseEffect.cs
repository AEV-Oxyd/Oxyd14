using System.Linq;
using Content.Shared.EntityEffects;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Reports every cruciform-bearing body the caster can see within the litany range.</summary>
public sealed partial class LitanyCruciformSenseEffect : LitanyEffect<LitanyCruciformSenseEffect>
{
    public LitanyCruciformSenseEffect()
    {
        Conditions =
        [
            new LitanySeesFollowersCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Cast-scoped: narrates every cruciform bearer the caster can see.</summary>
public sealed partial class LitanyCruciformSenseEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyCruciformSenseEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyCruciformSenseEffect> args)
    {
        var range = _litany.GetCastContext(args.User) is { } ctx ? _litany.GetSenseRange(ctx.Litany) : 0f;
        var visible = _litany.CollectVisibleActiveFollowers(entity.Owner, range);
        if (visible.Count == 0)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        foreach (var follower in visible.OrderBy(uid => uid))
        {
            var escaped = FormattedMessage.EscapeText(_litany.GetName(follower, entity.Owner));
            _litany.DeliverSocialNotice(entity.Owner,
                Loc.GetString("oxyd-litany-cruciform-sense-notice", ("name", escaped)));
        }

        _litany.ReportApplyResult(args.User, true);
    }
}
