using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:151-168</c> (power_biogen_awake): the biogenerator is switched on
/// or off from its screen. The switch is server-side, so the effect raises
/// <see cref="LitanyToggleBiogeneratorEvent"/> on the machine; the server
/// <c>BiogeneratorSystem</c> flips its working state.
/// </summary>
public sealed partial class LitanyPowerBiogeneratorEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyBiogenerator))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        foreach (var target in context.Targets)
        {
            if (!system.IsLitanyBiogenerator(target))
                continue;

            var toggle = new LitanyToggleBiogeneratorEvent(target, false);
            system.RaiseOn(target, ref toggle);
            if (toggle.Handled)
                return true;
        }

        return false;
    }
}
