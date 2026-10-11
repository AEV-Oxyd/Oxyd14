using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;
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

/// <summary>Raised once on the caster: the first resolved human takes the vision.</summary>
public sealed partial class LitanyRevelationEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyRevelationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyRevelationEffect> args)
    {
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is not { } target)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var amount = _litany.RollInclusive(0, LitanyRevelationEffect.MaxSanityGain);
        var delta = new LitanySanityDeltaEvent(target, amount, false);
        _litany.RaiseOn(target, ref delta);
        if (!delta.Handled)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (args.User is { } user)
        {
            var revelation = new NeoTheologyRevelationEvent(user, target);
            _litany.RaiseOn(user, ref revelation);
        }

        _litany.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-revelation-vision"));
        _litany.ReportApplyResult(args.User, true);
    }
}
