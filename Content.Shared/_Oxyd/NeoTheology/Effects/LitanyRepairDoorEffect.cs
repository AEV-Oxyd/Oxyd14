using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:100-145</c> (repair_door): the damaged holy door is fully healed
/// for the biomatter lying within the caster's reach. Both halves are server-side, so the effect
/// raises <see cref="LitanyRepairDoorEvent"/> on the door; the server
/// <c>NeoTheologyDoorSystem</c> calls its own <c>TryRepair</c> with the caster and the ritual
/// cost.
/// </summary>
public sealed partial class LitanyRepairDoorEffect : LitanyEffect
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
            if (!system.IsLitanyDoor(target))
                continue;

            var repair = new LitanyRepairDoorEvent(target, context.User, false, validateOnly);
            system.RaiseOn(target, ref repair);
            if (repair.Handled)
                return true;
        }

        return false;
    }
}
