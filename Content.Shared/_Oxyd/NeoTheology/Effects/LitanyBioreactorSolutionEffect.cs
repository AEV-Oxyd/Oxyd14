using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:200-213</c> (bioreactor solution): the closed chamber is pumped
/// in or out. The pump is server-side, so the effect raises
/// <see cref="LitanyPumpBioreactorEvent"/> on the reactor; the server <c>BioreactorSystem</c>
/// calls its own <c>TryPumpSolution</c>.
/// </summary>
public sealed partial class LitanyBioreactorSolutionEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyBioreactor))
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
            if (!system.IsLitanyBioreactor(target))
                continue;

            var pump = new LitanyPumpBioreactorEvent(target, false);
            system.RaiseOn(target, ref pump);
            if (pump.Handled)
                return true;
        }

        return false;
    }
}
