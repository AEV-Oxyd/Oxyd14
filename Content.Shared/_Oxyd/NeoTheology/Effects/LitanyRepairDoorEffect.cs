using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:100-145</c> (repair_door): the damaged holy door is fully healed
/// for the biomatter lying within the caster's reach. Both halves are server-side, so the effect
/// raises <see cref="LitanyRepairDoorEvent"/> on the door; the server
/// <c>NeoTheologyDoorSystem</c> calls its own <c>TryRepair</c> with the caster and the ritual
/// cost.
/// </summary>
public sealed partial class LitanyRepairDoorEffect : LitanyEffect<LitanyRepairDoorEffect>
{
    public LitanyRepairDoorEffect()
    {
        Conditions =
        [
            new LitanyRepairableDoorCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: the first repairable holy door gets healed.</summary>
public sealed partial class LitanyRepairDoorEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyRepairDoorEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyRepairDoorEffect> args)
    {
        var handled = false;
        if (args.User is { } user && _litany.GetCastContext(user) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!_litany.IsLitanyDoor(target))
                    continue;

                var repair = new LitanyRepairDoorEvent(target, user, false, false);
                _litany.RaiseOn(target, ref repair);
                if (repair.Handled)
                {
                    handled = true;
                    break;
                }
            }
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
