using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:291-309</c> (Knowledge): the inquisitor reads how many
/// telecrystals remain in the hidden uplink. The server <c>NtUplinkSystem</c> owns the store
/// and sends the count, or "no uplink" when the module is absent.
/// </summary>
public sealed partial class LitanyKnowledgeEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        var probe = new LitanyUplinkReportEvent(context.User, true, false);
        system.RaiseOn(context.User, ref probe);

        failure = probe.Handled ? null : "oxyd-litany-uplink-none";
        return probe.Handled;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var report = new LitanyUplinkReportEvent(context.User, false, false);
        system.RaiseOn(context.User, ref report);
        return report.Handled;
    }
}
