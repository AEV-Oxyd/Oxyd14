using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:173-211</c> (Atonement) and <c>rituals/inquisitor.dm:33-65</c>
/// (Penance): extreme pain with no real harm. The fork has no nonphysical pain value, so the
/// server handler maps the Eris <c>adjustHalLoss(50)</c> rider to stamina damage — a named
/// divergence from the plan's "new capability" row.
/// </summary>
public sealed partial class LitanyPainEffect : LitanyEffect
{
    [DataField]
    public float Amount = 50f;

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
            var pain = new LitanyPainEvent(target, Amount, false);
            system.RaiseOn(target, ref pain);
            handled |= pain.Handled;
        }

        return handled;
    }
}
