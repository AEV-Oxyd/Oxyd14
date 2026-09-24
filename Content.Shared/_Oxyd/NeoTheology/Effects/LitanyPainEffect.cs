using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:173-211</c> (Atonement) and <c>rituals/inquisitor.dm:33-65</c>
/// (Penance): <c>adjustHalLoss(50)</c> adds temporary pain without wound or stamina damage.
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
