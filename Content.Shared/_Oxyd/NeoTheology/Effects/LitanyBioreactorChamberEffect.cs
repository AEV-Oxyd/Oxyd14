using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:219-236</c> (bioreactor chamber doors): the chamber door is
/// opened or shut after re-checking what jams it. The check and the toggle are server-side, so
/// the effect raises <see cref="LitanyToggleBioreactorChamberEvent"/> on the reactor; the server
/// <c>BioreactorSystem</c> calls its own <c>TryToggleChamber</c>.
/// </summary>
public sealed partial class LitanyBioreactorChamberEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!Execute(system, context, true))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
        => Execute(system, context, false);

    private bool Execute(LitanyEffectSystem system, LitanyEffectContext context, bool validateOnly)
    {
        foreach (var target in context.Targets)
        {
            if (!system.IsLitanyBioreactor(target))
                continue;

            var toggle = new LitanyToggleBioreactorChamberEvent(target, false, validateOnly);
            system.RaiseOn(target, ref toggle);
            if (toggle.Handled)
                return true;
        }

        return false;
    }
}
