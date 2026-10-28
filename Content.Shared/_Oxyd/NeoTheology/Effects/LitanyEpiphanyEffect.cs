using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:31-60</c> (baptism): the target must be a living body with an
/// installed, not-yet-active cruciform; the litany activates it. Activation is server
/// gameplay, so the effect raises <see cref="LitanyActivateCruciformEvent"/> on the target
/// and the server CruciformSystem does the work.
/// </summary>
public sealed partial class LitanyEpiphanyEffect : LitanyEffect<LitanyEpiphanyEffect>
{
    /// <summary>The pipeline delivers each resolved candidate; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyEpiphanyEffect()
    {
        Conditions =
        [
            new LitanyAliveCondition { FailureId = "oxyd-litany-no-target" },
            // Reports no-cruciform when no implant sits in the body, already-active when it
            // has fired before — matching the Eris gate order.
            new LitanyInactiveCruciformCondition(),
        ];
    }
}

/// <summary>Activates each resolved body's dormant cruciform.</summary>
public sealed partial class LitanyEpiphanyEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyEpiphanyEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyEpiphanyEffect> args)
    {
        var activate = new LitanyActivateCruciformEvent(entity.Owner, false);
        _litany.RaiseOn(entity.Owner, ref activate);
        _litany.ReportApplyResult(args.User, activate.Handled);
    }
}
