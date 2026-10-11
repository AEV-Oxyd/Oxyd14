using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:189-231</c> (Scrying): the caster watches a station follower's
/// surroundings for a bounded duration. The session is server-only, so the effect raises
/// <see cref="LitanyScryingEvent"/> on the scried body; <c>ScryingSystem</c> starts its own
/// bounded session and sets <c>Handled</c>.
/// </summary>
public sealed partial class LitanyScryingEffect : LitanyEffect<LitanyScryingEffect>
{
    /// <summary>Eris hard-codes 300 ds (30 s); the YAML effectDuration carries the same value.</summary>
    internal static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(30);

    public LitanyScryingEffect()
    {
        Conditions =
        [
            new LitanyScryableCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: binds the caster's eye to the first resolved body.</summary>
public sealed partial class LitanyScryingEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyScryingEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyScryingEffect> args)
    {
        var handled = false;
        if (args.User is { } user &&
            _litany.GetCastContext(user) is { Targets.Count: > 0 } context)
        {
            // Eris lets the caster pick a disciple. The fork's list is uid-sorted, so
            // its first entry is a deterministic pick.
            var target = context.Targets[0];
            var duration = context.Litany.EffectDuration > TimeSpan.Zero
                ? context.Litany.EffectDuration
                : LitanyScryingEffect.FallbackDuration;

            var ev = new LitanyScryingEvent(user, target, duration, false, false);
            _litany.RaiseOn(target, ref ev);
            handled = ev.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
