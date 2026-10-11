using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Sanctify (Eris <c>rituals/group.dm:200-218</c>): the area the starter treads is sanctified and
/// every obelisk is forced active for at least sixty seconds. Eris also raises the area sanctify
/// signal; no fork consumer exists for it (named divergence), so only the obelisk part runs.
/// </summary>
public sealed partial class LitanySanctifyEffect : LitanyCeremonyEffect<LitanySanctifyEffect>
{
    /// <summary>Eris <c>O.force_active = max(60, O.force_active)</c>.</summary>
    [DataField]
    public TimeSpan ForceActiveTime = TimeSpan.FromSeconds(60);

    /// <summary>Eris <c>high_ritual = FALSE</c>: any bearer may start it.</summary>
    public override bool RequiresClergy => false;
}

/// <summary>Raised once on the starter: forces every obelisk active for the window.</summary>
public sealed partial class LitanySanctifyEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanySanctifyEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanySanctifyEffect> args)
    {
        var sanctify = new LitanySanctifyAreaEvent(entity, args.Effect.ForceActiveTime, false);
        _litany.RaiseOn(entity, ref sanctify);
        _litany.ReportApplyResult(args.User, sanctify.Handled);
    }
}
