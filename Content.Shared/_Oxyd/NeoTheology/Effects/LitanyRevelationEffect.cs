using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Components;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:158-175</c>: the caster grants a nearby human a vision. Owner
/// decision: no hallucination — the target gains <c>rand(0, 10)</c> sanity (Belief source)
/// and receives one vision message. The sanity apply lives on the server-only SanitySystem,
/// reached through <see cref="LitanySanityDeltaEvent"/>.
/// </summary>
public sealed partial class LitanyRevelationEffect : LitanyEffect<LitanyRevelationEffect>
{
    /// <summary>Each resolved human takes the vision.</summary>
    public override bool PerTarget => true;

    public LitanyRevelationEffect()
    {
        Conditions =
        [
            new LitanySanityCapableCondition { FailureId = "oxyd-litany-no-sanity" },
            new LitanyHolyInfluenceAcceptedCondition { FailureId = "oxyd-litany-biological-rejection" },
        ];
    }

    /// <summary>Eris <c>rand(0,10)</c> upper bound for the Belief gain.</summary>
    public const int MaxSanityGain = 10;
}

/// <summary>Applies the sanity gain and delivers the vision on each resolved target.</summary>
public sealed partial class LitanyRevelationEntityEffectSystem
    : EntityEffectSystem<MobStateComponent, LitanyRevelationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<LitanyRevelationEffect> args)
    {
        var amount = _litany.RollInclusive(0, LitanyRevelationEffect.MaxSanityGain);
        var delta = new LitanySanityDeltaEvent(entity, amount, false);
        _litany.RaiseOn(entity, ref delta);
        if (!delta.Handled)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (args.User is { } user)
        {
            var revelation = new NeoTheologyRevelationEvent(user, entity);
            _litany.RaiseOn(user, ref revelation);
        }

        _litany.DeliverSocialNotice(entity, Loc.GetString("oxyd-litany-revelation-vision"));
        _litany.ReportApplyResult(args.User, true);
    }
}
