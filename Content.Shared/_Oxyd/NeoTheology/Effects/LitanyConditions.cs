using Content.Shared.Damage.Components;
using Content.Shared.EntityConditions;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Base for the litany predicate conditions. A failed check records its
/// <see cref="FailureId"/> on the caster's <see cref="LitanyCastContextComponent"/> so
/// <c>TryValidateEffects</c> surfaces the same reason the old CanApply returned.
/// </summary>
public abstract partial class LitanyCondition<T> : EntityConditionBase<T> where T : LitanyCondition<T>
{
    /// <summary>Localization id the validate pass reports when this condition fails.</summary>
    [DataField]
    public LocId? FailureId;

    /// <summary>Litany rites never appear in the reagent guidebook.</summary>
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public abstract partial class LitanyConditionSystem<TComp, TCon> : EntityConditionSystem<TComp, TCon>
    where TComp : Component where TCon : LitanyCondition<TCon>
{
    [Dependency] protected LitanyEffectSystem Litany = default!;

    protected bool Check(EntityUid? sourceEnt, TCon condition, bool passes)
    {
        if (!passes && condition.FailureId is { } failure)
            Litany.ReportValidationFailure(sourceEnt, failure);
        return passes;
    }
}

/// <summary>Target is a live mob.</summary>
public sealed partial class LitanyAliveCondition : LitanyCondition<LitanyAliveCondition>;

public sealed partial class LitanyAliveConditionSystem : LitanyConditionSystem<MobStateComponent, LitanyAliveCondition>
{
    [Dependency] private MobStateSystem _mobState = default!;

    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyAliveCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, _mobState.IsAlive(entity));
    }
}

/// <summary>Target is a dead mob (Deprivation's extraction; resurrection flows).</summary>
public sealed partial class LitanyDeadCondition : LitanyCondition<LitanyDeadCondition>;

public sealed partial class LitanyDeadConditionSystem : LitanyConditionSystem<MobStateComponent, LitanyDeadCondition>
{
    [Dependency] private MobStateSystem _mobState = default!;

    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyDeadCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, _mobState.IsDead(entity));
    }
}

/// <summary>Target can receive damage: damageable and not godmode.</summary>
public sealed partial class LitanyCanReceiveDamageCondition : LitanyCondition<LitanyCanReceiveDamageCondition>;

public sealed partial class LitanyCanReceiveDamageConditionSystem
    : LitanyConditionSystem<DamageableComponent, LitanyCanReceiveDamageCondition>
{
    protected override void Condition(Entity<DamageableComponent> entity, ref EntityConditionEvent<LitanyCanReceiveDamageCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, !HasComp<GodmodeComponent>(entity));
    }
}

/// <summary>Target has an installed cruciform implant (active or not).</summary>
public sealed partial class LitanyInstalledCruciformCondition : LitanyCondition<LitanyInstalledCruciformCondition>;

public sealed partial class LitanyInstalledCruciformConditionSystem
    : LitanyConditionSystem<CruciformBearerComponent, LitanyInstalledCruciformCondition>
{
    protected override void Condition(Entity<CruciformBearerComponent> entity, ref EntityConditionEvent<LitanyInstalledCruciformCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciformEntity(entity, out _, out _));
    }
}

/// <summary>Target is the caster — restricts an effect to self when the resolve pass includes others.</summary>
public sealed partial class LitanySelfTargetCondition : LitanyCondition<LitanySelfTargetCondition>;

public sealed partial class LitanySelfTargetConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySelfTargetCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySelfTargetCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, entity.Owner == args.SourceEnt);
    }
}
