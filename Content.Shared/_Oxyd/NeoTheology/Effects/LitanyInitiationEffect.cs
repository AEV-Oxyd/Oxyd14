using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:259-289</c> (Initiation): the ascension kit's convert module is
/// activated and the target is promoted to Preacher. The kit item installs the module and
/// the ritual activates it. The rank swap itself is server-only, reached through
/// <see cref="LitanyInitiationEvent"/>.
/// </summary>
public sealed partial class LitanyInitiationEffect : LitanyEffect<LitanyInitiationEffect>
{
    private static readonly ProtoId<CoreModulePrototype> PriestRankModule = NeoTheologyPrototypes.PriestModule;
    private static readonly ProtoId<CoreModulePrototype> InquisitorRankModule = NeoTheologyPrototypes.InquisitorModule;

    /// <summary>The pipeline delivers each resolved candidate; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyInitiationEffect()
    {
        Conditions =
        [
            new LitanyHasActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
            // Eris: "The target is already a preacher."
            new LitanyNotPreacherCondition { FailureId = "oxyd-litany-initiation-already-preacher" },
            new LitanyCruciformModuleCondition
            {
                Module = NeoTheologyPrototypes.PriestConvertModule,
                FailureId = "oxyd-litany-initiation-no-kit",
            },
        ];
    }
}

/// <summary>Promotes each resolved candidate through the ascension kit's convert module.</summary>
public sealed partial class LitanyInitiationEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyInitiationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyInitiationEffect> args)
    {
        var ev = new LitanyInitiationEvent(args.User ?? entity.Owner, false);
        _litany.RaiseOn(entity.Owner, ref ev);
        if (ev.Handled && _litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.Medium, args.User ?? entity.Owner,
                $"initiated via {ctx.Litany.ID} on", entity.Owner);
        }

        _litany.ReportApplyResult(args.User, ev.Handled);
    }
}
