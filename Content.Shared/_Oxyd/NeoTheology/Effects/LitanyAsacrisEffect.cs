using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:68-90</c> (Asacris): removable upgrade modules attached to
/// the target's cruciform are removed. Rank modules and physical upgrades remain.
/// The removal is server-side, so the effect raises
/// <see cref="LitanyRemoveUpgradesEvent"/> on the body.
/// </summary>
public sealed partial class LitanyAsacrisEffect : LitanyEffect<LitanyAsacrisEffect>
{
    /// <summary>The pipeline delivers each resolved bearer; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyAsacrisEffect()
    {
        Conditions =
        [
            // Eris refuses when the implant is absent OR holds no removable upgrades; both
            // report the same no-effect line.
            new LitanyHasCoreUpgradesCondition { FailureId = "oxyd-litany-no-effect" },
        ];
    }
}

/// <summary>Strips removable core upgrades from each resolved bearer's cruciform.</summary>
public sealed partial class LitanyAsacrisEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyAsacrisEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyAsacrisEffect> args)
    {
        var strip = new LitanyRemoveUpgradesEvent(entity.Owner, false);
        _litany.RaiseOn(entity.Owner, ref strip);
        if (strip.Handled && _litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.Medium, args.User ?? entity.Owner,
                $"stripped upgrades via {ctx.Litany.ID} on", entity.Owner);
        }

        _litany.ReportApplyResult(args.User, strip.Handled);
    }
}
