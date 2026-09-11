using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:45</c> (construction): the caster picks a blueprint,
/// the ritual checks the materials lying on the tile in front, spends them and raises
/// the structure. The server <c>NeoTheologyConstructionSystem</c> owns both halves, so
/// <see cref="CanApply"/> raises a validate-only bridge event and
/// <see cref="Apply"/> raises the real one.
/// </summary>
public sealed partial class LitanyManifestationEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // The first validation pass runs before the caster picks a blueprint. The
        // post-choice pass (SubmitChoicesCore) revalidates with the selection.
        if (context.SelectedBlueprint is not { } blueprint)
        {
            failure = null;
            return true;
        }

        var check = new LitanyManifestationEvent(context.User, blueprint, true, false);
        system.RaiseOn(context.User, ref check);
        failure = check.Failure;
        return check.Handled;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.SelectedBlueprint is not { } blueprint)
            return false;

        var build = new LitanyManifestationEvent(context.User, blueprint, false, false);
        system.RaiseOn(context.User, ref build);
        return build.Handled;
    }
}
