using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:92</c> (deconstruction): the ritual finds the
/// NeoTheology construction on the tile in front, returns its materials and deletes it.
/// The server <c>NeoTheologyConstructionSystem</c> owns the catalog lookup and the
/// refund, so <see cref="CanApply"/> raises a validate-only bridge event.
/// </summary>
public sealed partial class LitanyUprootEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        var check = new LitanyUprootEvent(context.User, true, false);
        system.RaiseOn(context.User, ref check);
        failure = check.Failure;
        return check.Handled;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var uproot = new LitanyUprootEvent(context.User, false, false);
        system.RaiseOn(context.User, ref uproot);
        return uproot.Handled;
    }
}
