using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:315-327</c> (Bounty): the inquisitor opens the hidden uplink
/// and orders supplies. The server <c>NtUplinkSystem</c> owns the store and opens its interface
/// from anywhere, because the uplink itself lives inside the cruciform.
/// </summary>
public sealed partial class LitanyBountyEffect : LitanyEffect<LitanyBountyEffect>
{
    public LitanyBountyEffect()
    {
        Conditions =
        [
            // The validate probe reports the uplink's own failure reason.
            new LitanyUplinkOpenCondition(),
        ];
    }
}

/// <summary>Cast-scoped: opens the caster's hidden uplink.</summary>
public sealed partial class LitanyBountyEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyBountyEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyBountyEffect> args)
    {
        var open = new LitanyUplinkOpenEvent(entity.Owner, ValidateOnly: false, Handled: false);
        _litany.RaiseOn(entity.Owner, ref open);
        _litany.ReportApplyResult(args.User, open.Handled);
    }
}
