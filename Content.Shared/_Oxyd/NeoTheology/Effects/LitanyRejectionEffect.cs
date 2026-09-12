using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:64-90</c> (Rejection): the caster sheds every foreign implant.
/// The purge is server-side, so the effect raises <see cref="LitanyRejectForeignBodyEvent"/>.
/// Eris also tears robotic limbs; the fork has no external-limb model, so the server handler
/// removes non-cruciform implants and damages the body instead.
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
