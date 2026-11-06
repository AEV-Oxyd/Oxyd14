using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:92-120</c> (Reveal Adversaries): the caster learns of hostile fauna
/// and traps nearby. The scan is server-side, so the effect raises
/// <see cref="LitanyRevealAdversariesEvent"/> on the caster.
/// </summary>
public sealed partial class LitanyRevealAdversariesEffect : LitanyEffect<LitanyRevealAdversariesEffect>
{
    /// <summary>Each resolved target gets the adversary scan (the self rite resolves the caster).</summary>
    public override bool PerTarget => true;
}

/// <summary>Raises the reveal bridge on each resolved target.</summary>
public sealed partial class LitanyRevealAdversariesEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyRevealAdversariesEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyRevealAdversariesEffect> args)
    {
        var reveal = new LitanyRevealAdversariesEvent(entity, false);
        _litany.RaiseOn(entity, ref reveal);
        _litany.ReportApplyResult(args.User, reveal.Handled);
    }
}
