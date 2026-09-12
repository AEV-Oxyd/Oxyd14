using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/agrolyte.dm:10-45</c> (Accelerated growth): every plant in view grows
/// faster for about five minutes. The boost is server-side, so the effect raises
/// <see cref="LitanyAcceleratedGrowthEvent"/> on the caster.
/// </summary>
public sealed partial class LitanyAcceleratedGrowthEffect : LitanyEffect
{
    /// <summary>Eris <c>boost_value</c>: the aging-process multiplier.</summary>
    [DataField]
    public float Multiplier = 1.5f;

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
            var growth = new LitanyAcceleratedGrowthEvent(target, Multiplier, context.Litany.EffectDuration, false);
            system.RaiseOn(target, ref growth);
            handled |= growth.Handled;
        }

        return handled;
    }
}
