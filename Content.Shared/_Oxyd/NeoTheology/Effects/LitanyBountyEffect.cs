using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:315-327</c> (Bounty): the inquisitor opens the hidden uplink
/// and orders supplies. The server <c>NtUplinkSystem</c> owns the store and opens its interface
/// from anywhere, because the uplink itself lives inside the cruciform.
/// </summary>
public sealed partial class LitanyBountyEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        var probe = new LitanyUplinkOpenEvent(context.User, true, false);
        system.RaiseOn(context.User, ref probe);

        if (probe.Handled)
        {
            failure = null;
            return true;
        }

        failure = probe.Failure ?? "oxyd-litany-uplink-none";
        return false;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var open = new LitanyUplinkOpenEvent(context.User, false, false);
        system.RaiseOn(context.User, ref open);
        return open.Handled;
    }
}
