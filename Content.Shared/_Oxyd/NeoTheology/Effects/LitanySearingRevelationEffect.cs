using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Searing Revelation (Eris <c>rituals/crusader.dm:54-78</c>): a psy-wave knocks down every
/// creature in view that bears no cruciform, and the caster may fall too. The rolls and the
/// knockdown are server gameplay, so the effect raises
/// <see cref="LitanySearingRevelationEvent"/> on the caster.
/// </summary>
public sealed partial class LitanySearingRevelationEffect : LitanyEffect<LitanySearingRevelationEffect>
{
    public LitanySearingRevelationEffect()
    {
        Conditions =
        [
            // Eris CanApply checked only system.CanReceiveSkillBuff(context.User).
            new LitanyFirstTargetCanReceiveSkillBuffCondition(),
        ];
    }

    /// <summary>Eris scans <c>view(user)</c>; the fork scans this radius.</summary>
    [DataField]
    public float Radius = 7f;

    /// <summary>Eris <c>flash</c>: the caster falls.</summary>
    [DataField]
    public TimeSpan SelfKnockdown = TimeSpan.FromSeconds(10);

    /// <summary>Eris <c>flash</c>: the others fall.</summary>
    [DataField]
    public TimeSpan VictimKnockdown = TimeSpan.FromSeconds(5);
}

/// <summary>Raised once on the caster: the psy-wave knocks down the cruciform-less in view.</summary>
public sealed partial class LitanySearingRevelationEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanySearingRevelationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanySearingRevelationEffect> args)
    {
        var flash = new LitanySearingRevelationEvent(
            entity, args.Effect.Radius, args.Effect.SelfKnockdown, args.Effect.VictimKnockdown, false);
        _litany.RaiseOn(entity, ref flash);
        _litany.ReportApplyResult(args.User, flash.Handled);
    }
}
