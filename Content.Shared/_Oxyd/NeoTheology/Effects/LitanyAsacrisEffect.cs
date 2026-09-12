using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:68-90</c> (Asacris): every upgrade attached to the target's
/// cruciform is removed. The removal is server-side, so the effect raises
/// <see cref="LitanyRemoveUpgradesEvent"/> on the body.
/// </summary>
public sealed partial class LitanyAsacrisEffect : LitanyEffect
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
            var strip = new LitanyRemoveUpgradesEvent(target, false);
            system.RaiseOn(target, ref strip);
            handled |= strip.Handled;
        }

        return handled;
    }
}
