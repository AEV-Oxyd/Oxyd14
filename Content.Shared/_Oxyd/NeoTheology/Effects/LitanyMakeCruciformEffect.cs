using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:43-75</c> (cruciformforge): the forge starts a produce run for
/// the materials it has already banked. The material check and the run are server-side, so the
/// effect raises <see cref="LitanyForgeProduceEvent"/> on the forge; the server
/// <c>CruciformForgeSystem</c> calls its own <c>TryProduce</c>.
/// </summary>
public sealed partial class LitanyMakeCruciformEffect : LitanyEffect
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
            if (!system.IsLitanyForge(target))
                continue;

            var produce = new LitanyForgeProduceEvent(target, false, validateOnly);
            system.RaiseOn(target, ref produce);
            if (produce.Handled)
                return true;
        }

        return false;
    }
}
