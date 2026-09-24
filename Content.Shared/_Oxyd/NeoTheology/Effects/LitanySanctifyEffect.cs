using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Sanctify (Eris <c>rituals/group.dm:200-218</c>): the area the starter treads is sanctified and
/// every obelisk is forced active for at least sixty seconds. Eris also raises the area sanctify
/// signal; no fork consumer exists for it (named divergence), so only the obelisk part runs.
/// </summary>
public sealed partial class LitanySanctifyEffect : LitanyCeremonyEffect
{
    /// <summary>Eris <c>O.force_active = max(60, O.force_active)</c>.</summary>
    [DataField]
    public TimeSpan ForceActiveTime = TimeSpan.FromSeconds(60);

    /// <summary>Eris <c>high_ritual = FALSE</c>: any bearer may start it.</summary>
    public override bool RequiresClergy => false;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var sanctify = new LitanySanctifyAreaEvent(context.User, ForceActiveTime, false);
        system.RaiseOn(context.User, ref sanctify);
        return sanctify.Handled;
    }
}
