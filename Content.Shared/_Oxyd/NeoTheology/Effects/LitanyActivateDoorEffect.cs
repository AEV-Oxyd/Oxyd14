using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>lock_door</c> (machinery.dm): toggles the bolt on the holy NeoTheology door
/// the caster faces. Ordinary station doors are not valid targets.
/// </summary>
public sealed partial class LitanyActivateDoorEffect : LitanyEffect<LitanyActivateDoorEffect>
{
    public LitanyActivateDoorEffect()
    {
        Conditions =
        [
            // At least one faced door must toggle; a broken door reports door-off instead.
            new LitanyDoorToggleableCondition(),
        ];
    }
}

/// <summary>
/// Cast-scoped: toggles the first toggleable holy door among the resolved machine targets.
/// </summary>
public sealed partial class LitanyActivateDoorEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyActivateDoorEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyActivateDoorEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Eris refuses the rite when any faced holy door is broken — a list-level veto the
        // per-target condition cannot express on its own.
        foreach (var target in ctx.Targets)
        {
            if (_litany.IsHolyDoorBroken(target))
            {
                _litany.ReportApplyResult(args.User, false);
                return;
            }
        }

        foreach (var target in ctx.Targets)
        {
            if (_litany.TryToggleLitanyDoor(target, entity.Owner))
            {
                _litany.ReportApplyResult(args.User, true);
                return;
            }
        }

        _litany.ReportApplyResult(args.User, false);
    }
}
