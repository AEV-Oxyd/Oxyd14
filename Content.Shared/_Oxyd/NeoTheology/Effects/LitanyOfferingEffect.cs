using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:232-320</c> (the offering family): the priest faces the Eye of the
/// Protector and names an offering; the altar collects the required items and the Eye banks them.
/// Both halves are server-side, so the effect raises <see cref="LitanyOfferingEvent"/> on the Eye
/// and the server <c>AltarSystem</c> runs its own <c>TryMakeOffering</c>. One class serves
/// DivineIntervention and HolyGuidance — they differ only in their offering prototype.
/// </summary>
public sealed partial class LitanyOfferingEffect : LitanyEffect
{
    /// <summary>The <c>oxydOffering</c> prototype this litany makes, e.g. <c>OxydNtOfferingDivineIntervention</c>.</summary>
    [DataField(required: true)]
    public string Offering = string.Empty;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyEye))
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
            if (!system.IsLitanyEye(target))
                continue;

            var offering = new LitanyOfferingEvent(context.User, Offering, false);
            system.RaiseOn(target, ref offering);
            if (offering.Handled)
                return true;
        }

        return false;
    }
}
