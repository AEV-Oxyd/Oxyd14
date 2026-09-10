using System.Linq;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>lock_door</c> (machinery.dm): toggles the bolt on the holy NeoTheology door
/// the caster faces. Ordinary station doors are not valid targets.
/// </summary>
public sealed partial class LitanyActivateDoorEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyDoor))
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
            if (system.TryToggleLitanyDoor(target, context.User))
                return true;
        }

        return false;
    }
}
