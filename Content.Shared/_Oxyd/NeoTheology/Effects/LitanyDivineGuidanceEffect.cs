using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:28</c> (blueprint_check): the caster picks a
/// NeoTheology blueprint, and the ritual prints that blueprint's material list. The
/// server <c>NeoTheologyConstructionSystem</c> owns the catalog and the message.
/// </summary>
public sealed partial class LitanyDivineGuidanceEffect : LitanyEffect<LitanyDivineGuidanceEffect>;

/// <summary>
/// Cast-scoped: prints the picked blueprint's material list. The first validation pass runs
/// before the caster picks a blueprint, so the effect carries no conditions.
/// </summary>
public sealed partial class LitanyDivineGuidanceEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyDivineGuidanceEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyDivineGuidanceEffect> args)
    {
        if (_litany.GetCastContext(args.User)?.SelectedBlueprint is not { } blueprint)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var info = new LitanyBlueprintInfoEvent(entity.Owner, blueprint, false);
        _litany.RaiseOn(entity.Owner, ref info);
        _litany.ReportApplyResult(args.User, info.Handled);
    }
}
