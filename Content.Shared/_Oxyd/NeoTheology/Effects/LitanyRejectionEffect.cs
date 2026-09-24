using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:64-90</c> (Rejection): the caster sheds robotic organs and foreign implants.
/// The server handles <see cref="LitanyRejectForeignBodyEvent"/> through native body and container APIs.
/// It preserves the detached entities, natural organs, and cruciform.
/// </summary>
public sealed partial class LitanyRejectionEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // Eris succeeds even when there is nothing to reject; the handler still runs.
        failure = null;
        return context.Targets.Count > 0;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var handled = false;
        foreach (var target in context.Targets)
        {
            var shed = new LitanyRejectForeignBodyEvent(target, false);
            system.RaiseOn(target, ref shed);
            handled |= shed.Handled;
        }

        return handled;
    }
}
