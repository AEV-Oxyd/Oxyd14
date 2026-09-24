using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:92-120</c> (Reveal Adversaries): the caster learns of hostile fauna
/// and traps nearby. The scan is server-side, so the effect raises
/// <see cref="LitanyRevealAdversariesEvent"/> on the caster.
/// </summary>
public sealed partial class LitanyRevealAdversariesEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = null;
        return context.Targets.Count > 0;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var handled = false;
        foreach (var target in context.Targets)
        {
            var reveal = new LitanyRevealAdversariesEvent(target, false);
            system.RaiseOn(target, ref reveal);
            handled |= reveal.Handled;
        }

        return handled;
    }
}
