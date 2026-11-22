using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/construction.dm:45</c> (construction): the caster picks a blueprint,
/// the ritual checks the materials lying on the tile in front, spends them and raises
/// the structure. The server <c>NeoTheologyConstructionSystem</c> owns both halves, so
/// <see cref="LitanyManifestationReadyCondition"/> raises a validate-only bridge event and
/// the handler raises the real one.
/// </summary>
public sealed partial class LitanyManifestationEffect : LitanyEffect<LitanyManifestationEffect>
{
    public LitanyManifestationEffect()
    {
        Conditions =
        [
            new LitanyManifestationReadyCondition(),
        ];
    }
}

/// <summary>Raised once on the caster: builds the picked blueprint on the tile in front.</summary>
public sealed partial class LitanyManifestationEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyManifestationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyManifestationEffect> args)
    {
        if (_litany.GetCastContext(args.User)?.SelectedBlueprint is not { } blueprint ||
            args.User is not { } user)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var build = new LitanyManifestationEvent(user, blueprint, false, false);
        _litany.RaiseOn(user, ref build);
        _litany.ReportApplyResult(args.User, build.Handled);
    }
}
