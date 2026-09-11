using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eternal Brotherhood (Eris <c>rituals/crusader.dm:11-20</c>): toggles the disciple HUD module
/// on and off. The toggle is server gameplay, so the effect raises
/// <see cref="LitanyToggleDiscipleHudEvent"/> on the caster.
/// </summary>
public sealed partial class LitanyEternalBrotherhoodEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var toggle = new LitanyToggleDiscipleHudEvent(context.User, false);
        system.RaiseOn(context.User, ref toggle);
        return toggle.Handled;
    }
}
