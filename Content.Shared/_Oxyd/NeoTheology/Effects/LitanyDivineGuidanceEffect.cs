using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:28</c> (blueprint_check): the caster picks a
/// NeoTheology blueprint, and the ritual prints that blueprint's material list. The
/// server <c>NeoTheologyConstructionSystem</c> owns the catalog and the message.
/// </summary>
public sealed partial class LitanyDivineGuidanceEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // The first validation pass runs before the caster picks a blueprint.
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.SelectedBlueprint is not { } blueprint)
            return false;

        var info = new LitanyBlueprintInfoEvent(context.User, blueprint, false);
        system.RaiseOn(context.User, ref info);
        return info.Handled;
    }
}
