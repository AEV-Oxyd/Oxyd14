using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:189-231</c> (Scrying): the caster watches a station follower's
/// surroundings for a bounded duration. The session is server-only, so the effect raises
/// <see cref="LitanyScryingEvent"/> on the scried body; <c>ScryingSystem</c> starts its own
/// bounded session and sets <c>Handled</c>.
/// </summary>
public sealed partial class LitanyScryingEffect : LitanyEffect
{
    /// <summary>Eris hard-codes 300 ds (30 s); the YAML effectDuration carries the same value.</summary>
    private static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(30);

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!Execute(system, context, true))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
        => Execute(system, context, false);

    private bool Execute(LitanyEffectSystem system, LitanyEffectContext context, bool validateOnly)
    {
        if (context.Targets.Count == 0)
            return false;

        // Eris lets the caster pick a disciple. The fork's StationFollower list is uid-sorted, so
        // its first entry is a deterministic pick.
        var target = context.Targets[0];
        var duration = context.Litany.EffectDuration > TimeSpan.Zero
            ? context.Litany.EffectDuration
            : FallbackDuration;

        var ev = new LitanyScryingEvent(context.User, target, duration, false, validateOnly);
        system.RaiseOn(target, ref ev);
        return ev.Handled;
    }
}
