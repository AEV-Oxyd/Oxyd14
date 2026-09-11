namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:233-252</c> (Sending): a telepathic message to the faithful —
/// anonymous unless the sender reveals themself. Eris let the caster pick one disciple and type
/// free text; the fork's litany UI has no text input and its StationFollower resolution is
/// uid-sorted, so the anonymous notice reaches every same-station follower (flagged divergence).
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
            system.DeliverSocialNotice(recipient, Loc.GetString("oxyd-litany-private-sending"));
            delivered = true;
        }

        return delivered;
    }
}
