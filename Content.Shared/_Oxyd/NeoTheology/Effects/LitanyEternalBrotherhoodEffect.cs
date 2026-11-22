using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eternal Brotherhood (Eris <c>rituals/crusader.dm:11-20</c>): toggles the disciple HUD module
/// on and off. The toggle is server gameplay, so the effect raises
/// <see cref="LitanyToggleDiscipleHudEvent"/> on the caster.
/// </summary>
public sealed partial class LitanyEternalBrotherhoodEffect : LitanyEffect<LitanyEternalBrotherhoodEffect>;

/// <summary>Cast-scoped: toggles the disciple HUD on the caster.</summary>
public sealed partial class LitanyEternalBrotherhoodEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyEternalBrotherhoodEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyEternalBrotherhoodEffect> args)
    {
        var toggle = new LitanyToggleDiscipleHudEvent(entity.Owner, false);
        _litany.RaiseOn(entity.Owner, ref toggle);
        _litany.ReportApplyResult(args.User, toggle.Handled);
    }
}
