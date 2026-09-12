namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:233-252</c> (Sending): a telepathic message to the faithful —
/// anonymous unless the sender reveals themself. Eris lets the caster pick one disciple and
/// type free text; the book UI now offers the same target pick and a text box, carried by
/// <see cref="LitanyEffectContext.Designation"/>'s sibling fields
/// <see cref="LitanyEffectContext.SelectedText"/>. Manual-speech casts have no picker, so the
/// historical prepared notice reaches every same-station follower (flagged fallback).
/// Delivery rides the shared social-notice helper Entreaty established.
/// </summary>
public sealed partial class LitanySendingEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0)
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var delivered = false;
        foreach (var recipient in context.Targets)
        {
            var message = context.SelectedText is { Length: > 0 } text
                ? Loc.GetString("oxyd-litany-private-sending-text", ("text", text))
                : Loc.GetString("oxyd-litany-private-sending");
            system.DeliverSocialNotice(recipient, message);
            delivered = true;
        }

        return delivered;
    }
}
