using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Mobs.Components;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/agrolyte.dm:10-45</c> (Accelerated growth): every plant in view grows
/// faster for about five minutes. The boost is server-side, so the effect raises
/// <see cref="LitanyAcceleratedGrowthEvent"/> on each resolved target.
/// </summary>
public sealed partial class LitanyAcceleratedGrowthEffect : LitanyEffect<LitanyAcceleratedGrowthEffect>
{
    /// <summary>The pipeline delivers each resolved target the boost is anchored on.</summary>
    public override bool PerTarget => true;

    public LitanyAcceleratedGrowthEffect()
    {
        Conditions =
        [
            new LitanyGrowthProbeCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }

    /// <summary>Eris <c>boost_value</c>: the aging-process multiplier.</summary>
    [DataField]
    public float Multiplier = 1.5f;
}

/// <summary>Raises <see cref="LitanyAcceleratedGrowthEvent"/> on each resolved target.</summary>
public sealed partial class LitanyAcceleratedGrowthEntityEffectSystem
    : EntityEffectSystem<MobStateComponent, LitanyAcceleratedGrowthEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<LitanyAcceleratedGrowthEffect> args)
    {
        var ctx = _litany.GetCastContext(args.User);
        var growth = new LitanyAcceleratedGrowthEvent(
            entity.Owner,
            args.Effect.Multiplier,
            ctx?.Litany.EffectDuration ?? TimeSpan.Zero,
            false);
        _litany.RaiseOn(entity.Owner, ref growth);
        _litany.ReportApplyResult(args.User, growth.Handled);
    }
}
