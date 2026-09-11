using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Searing Revelation (Eris <c>rituals/crusader.dm:54-78</c>): a psy-wave knocks down every
/// creature in view that bears no cruciform, and the caster may fall too. The rolls and the
/// knockdown are server gameplay, so the effect raises
/// <see cref="LitanySearingRevelationEvent"/> on the caster.
/// </summary>
public sealed partial class LitanySearingRevelationEffect : LitanyEffect
{
    /// <summary>Eris scans <c>view(user)</c>; the fork scans this radius.</summary>
    [DataField]
    public float Radius = 7f;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = null;
        return system.CanReceiveSkillBuff(context.User);
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var flash = new LitanySearingRevelationEvent(context.User, Radius, false);
        system.RaiseOn(context.User, ref flash);
        return flash.Handled;
    }
}
