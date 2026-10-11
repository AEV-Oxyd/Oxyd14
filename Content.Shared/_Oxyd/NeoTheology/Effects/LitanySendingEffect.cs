using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:233-252</c> (Sending): a telepathic message to the faithful —
/// anonymous unless the sender reveals themself. Eris lets the caster pick one disciple and
/// type free text; the book UI now offers the same target pick and a text box, carried by
/// <see cref="LitanyCastContextComponent.SelectedText"/>. Manual-speech casts have no picker, so the
/// historical prepared notice reaches every same-station follower (flagged fallback).
/// Delivery rides the shared social-notice helper Entreaty established.
/// </summary>
public sealed partial class LitanySendingEffect : LitanyEffect<LitanySendingEffect>
{
    public LitanySendingEffect()
    {
        Conditions =
        [
            new LitanyHasTargetsCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: delivers the sending notice to every resolved recipient.</summary>
public sealed partial class LitanySendingEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanySendingEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanySendingEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } context)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var delivered = false;
        foreach (var recipient in context.Targets)
        {
            var message = context.SelectedText is { Length: > 0 } text
                ? Loc.GetString("oxyd-litany-private-sending-text", ("text", text))
                : Loc.GetString("oxyd-litany-private-sending");
            _litany.DeliverSocialNotice(recipient, message);
            if (args.User is { } user)
                _litany.AdminLog(LogType.Chat, LogImpact.Low, user,
                    $"sent \"{context.SelectedText}\" via {context.Litany.ID} to", recipient);
            delivered = true;
        }

        _litany.ReportApplyResult(args.User, delivered);
    }
}
