using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:158-175</c>: the caster grants a nearby human a vision. Owner
/// decision: no hallucination — the target gains <c>rand(0, 10)</c> sanity (Belief source)
/// and receives one vision message. The sanity apply lives on the server-only SanitySystem,
/// reached through <see cref="LitanySanityDeltaEvent"/>.
/// </summary>
public sealed partial class LitanyRevelationEffect : LitanyEffect
{
    /// <summary>Eris <c>rand(0,10)</c> upper bound for the Belief gain.</summary>
    public const int MaxSanityGain = 10;

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 ||
            !system.IsAlive(context.Targets[0]) ||
            !system.CanReceiveSanityDelta(context.Targets[0]))
        {
            failure = "oxyd-litany-no-sanity";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];
        var amount = system.RollInclusive(0, MaxSanityGain);
        var delta = new LitanySanityDeltaEvent(target, amount, false);
        system.RaiseOn(target, ref delta);
        if (!delta.Handled)
            return false;

        system.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-revelation-vision"));
        return true;
    }
}
