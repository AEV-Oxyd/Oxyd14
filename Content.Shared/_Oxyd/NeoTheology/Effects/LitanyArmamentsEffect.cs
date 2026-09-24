using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:492-520</c> (Order armaments): the priest opens the authorised
/// armament shop from the Eye of the Protector. The fork's shop UI lives on the separate
/// <c>OxydNtArmamentsPrinter</c> (P2.16), so the effect opens that printer's existing BUI through
/// <see cref="LitanyOpenArmamentsEvent"/>; the point bank stays the printer's own concern.
/// </summary>
public sealed partial class LitanyArmamentsEffect : LitanyEffect
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
            if (!system.IsLitanyArmamentsPrinter(target))
                continue;

            var open = new LitanyOpenArmamentsEvent(context.User, false, validateOnly);
            system.RaiseOn(target, ref open);
            if (open.Handled)
                return true;
        }

        return false;
    }
}
