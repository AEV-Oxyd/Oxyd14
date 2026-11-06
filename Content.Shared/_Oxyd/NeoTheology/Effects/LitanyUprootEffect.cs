using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:92</c> (deconstruction): the ritual finds the
/// NeoTheology construction on the tile in front, returns its materials and deletes it.
/// The server <c>NeoTheologyConstructionSystem</c> owns the catalog lookup and the
/// refund, so <see cref="LitanyUprootableCondition"/> raises a validate-only bridge event.
/// </summary>
public sealed partial class LitanyUprootEffect : LitanyEffect<LitanyUprootEffect>
{
    public LitanyUprootEffect()
    {
        Conditions =
        [
            new LitanyUprootableCondition(),
        ];
    }
}

/// <summary>Raised once on the caster: refunds the construction on the tile in front.</summary>
public sealed partial class LitanyUprootEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyUprootEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyUprootEffect> args)
    {
        var uproot = new LitanyUprootEvent(entity, false, false);
        _litany.RaiseOn(entity, ref uproot);
        _litany.ReportApplyResult(args.User, uproot.Handled);
    }
}
