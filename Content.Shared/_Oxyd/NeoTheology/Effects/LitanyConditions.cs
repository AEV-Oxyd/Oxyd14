using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared.Damage.Components;
using Content.Shared.EntityConditions;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.Skills;
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

/// <summary>Target bears an installed, ACTIVE cruciform (Adoption/Confirmation/Excommunication/Initiation gates).</summary>
public sealed partial class LitanyActiveCruciformCondition : LitanyCondition<LitanyActiveCruciformCondition>;

public sealed partial class LitanyActiveCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyActiveCruciformCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyActiveCruciformCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetActiveCruciform(entity, out _));
    }
}

/// <summary>
/// Target bears an installed cruciform that has NOT activated yet (Epiphany's baptism
/// candidate). A body with no implant reports no-cruciform; an already-active one reports
/// already-active, matching the Eris gate order.
/// </summary>
public sealed partial class LitanyInactiveCruciformCondition : LitanyCondition<LitanyInactiveCruciformCondition>;

public sealed partial class LitanyInactiveCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyInactiveCruciformCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyInactiveCruciformCondition> args)
    {
        if (!Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform))
        {
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-cruciform");
            args.Result = false;
            return;
        }

        if (cruciform.Active)
        {
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-already-active");
            args.Result = false;
            return;
        }

        args.Result = true;
    }
}

/// <summary>Target has no installed cruciform at all (Commitment refuses a second implant).</summary>
public sealed partial class LitanyNoInstalledCruciformCondition : LitanyCondition<LitanyNoInstalledCruciformCondition>;

public sealed partial class LitanyNoInstalledCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyNoInstalledCruciformCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNoInstalledCruciformCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            !Litany.TryGetInstalledCruciformEntity(entity, out _, out _));
    }
}

/// <summary>Target's installed cruciform has at least one removable core upgrade (Asacris).</summary>
public sealed partial class LitanyHasCoreUpgradesCondition : LitanyCondition<LitanyHasCoreUpgradesCondition>;

public sealed partial class LitanyHasCoreUpgradesConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyHasCoreUpgradesCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyHasCoreUpgradesCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform) &&
            cruciform.CoreUpgrades.Count > 0);
    }
}

/// <summary>Target is the given species (Commitment's §5.2 v1 conversion restriction).</summary>
public sealed partial class LitanySpeciesCondition : LitanyCondition<LitanySpeciesCondition>
{
    /// <summary>The only eligible species; populated by the owning effect after deserialization.</summary>
    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species;
}

public sealed partial class LitanySpeciesConditionSystem
    : LitanyConditionSystem<HumanoidProfileComponent, LitanySpeciesCondition>
{
    protected override void Condition(Entity<HumanoidProfileComponent> entity, ref EntityConditionEvent<LitanySpeciesCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.SpeciesMatches(entity, args.Condition.Species));
    }
}

/// <summary>Target is buckled, lying down on a NeoTheology altar (optionally undressed).</summary>
public sealed partial class LitanyProcedureAltarCondition : LitanyCondition<LitanyProcedureAltarCondition>
{
    /// <summary>Require the patient to carry no clothing (Commitment's install).</summary>
    [DataField]
    public bool Undressed = true;
}

public sealed partial class LitanyProcedureAltarConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyProcedureAltarCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyProcedureAltarCondition> args)
    {
        var passes = Litany.TryGetProcedureAltar(entity, args.Condition.Undressed, out _, out var failure);
        if (!passes && failure is { } reason)
            Litany.ReportValidationFailure(args.SourceEnt, reason);

        args.Result = passes;
    }
}

/// <summary>A loose, never-activated cruciform rests on the procedure altar beside the target.</summary>
public sealed partial class LitanyAltarCruciformCondition : LitanyCondition<LitanyAltarCruciformCondition>;

public sealed partial class LitanyAltarCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyAltarCruciformCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyAltarCruciformCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryFindAltarCruciform(entity, out _, out _));
    }
}

/// <summary>Target's installed cruciform profile differs from the given one (Excommunication's inquisitor veto).</summary>
public sealed partial class LitanyNotProfileCondition : LitanyCondition<LitanyNotProfileCondition>
{
    /// <summary>The refused profile.</summary>
    [DataField(required: true)]
    public ProtoId<NeoTheologyProfilePrototype> Profile;
}

public sealed partial class LitanyNotProfileConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyNotProfileCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNotProfileCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform) &&
            cruciform.Profile != args.Condition.Profile);
    }
}

/// <summary>Target's cruciform carries neither the priest nor the inquisitor rank module.</summary>
public sealed partial class LitanyNotPreacherCondition : LitanyCondition<LitanyNotPreacherCondition>;

public sealed partial class LitanyNotPreacherConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyNotPreacherCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNotPreacherCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform) &&
            !cruciform.InstalledModules.Contains(NeoTheologyPrototypes.PriestModule) &&
            !cruciform.InstalledModules.Contains(NeoTheologyPrototypes.InquisitorModule));
    }
}

/// <summary>Target's installed cruciform carries the given core module (Initiation's ascension kit).</summary>
public sealed partial class LitanyCruciformModuleCondition : LitanyCondition<LitanyCruciformModuleCondition>
{
    /// <summary>The required installed module.</summary>
    [DataField(required: true)]
    public ProtoId<CoreModulePrototype> Module;
}

public sealed partial class LitanyCruciformModuleConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyCruciformModuleCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyCruciformModuleCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform) &&
            cruciform.InstalledModules.Contains(args.Condition.Module));
    }
}

/// <summary>Target does not carry the godblood mutation (Excommunication refuses Godblood bearers).</summary>
public sealed partial class LitanyNoGodbloodCondition : LitanyCondition<LitanyNoGodbloodCondition>;

public sealed partial class LitanyNoGodbloodConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNoGodbloodCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoGodbloodCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            !HasComp<GodbloodMutationComponent>(entity));
    }
}

/// <summary>Target does not carry the atheist mutation (reagents rejected by biological rejection).</summary>
public sealed partial class LitanyNotAtheistCondition : LitanyCondition<LitanyNotAtheistCondition>;

public sealed partial class LitanyNotAtheistConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNotAtheistCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNotAtheistCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            !HasComp<AtheistMutationComponent>(entity));
    }
}

/// <summary>Target is a live mob that can carry skill buffs (Call to Battle).</summary>
public sealed partial class LitanyCanReceiveSkillBuffCondition : LitanyCondition<LitanyCanReceiveSkillBuffCondition>;

public sealed partial class LitanyCanReceiveSkillBuffConditionSystem
    : LitanyConditionSystem<MobSkillComponent, LitanyCanReceiveSkillBuffCondition>
{
    protected override void Condition(Entity<MobSkillComponent> entity, ref EntityConditionEvent<LitanyCanReceiveSkillBuffCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, Litany.CanReceiveSkillBuff(entity));
    }
}

/// <summary>Subject can take a chemical dose of the given total volume (alive, bloodstream, capacity).</summary>
public sealed partial class LitanyCanInjectCondition : LitanyCondition<LitanyCanInjectCondition>
{
    /// <summary>The combined dose volume the bloodstream must accept.</summary>
    [DataField(required: true)]
    public FixedPoint2 MinVolume;
}

public sealed partial class LitanyCanInjectConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyCanInjectCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyCanInjectCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.CanInjectMedicine(entity, args.Condition.MinVolume));
    }
}

/// <summary>
/// The caster holds a blessable oddity: held item, not already blessed, and at least one
/// non-zero skill in its giving table (DivineBlessing's three checks in Eris order).
/// </summary>
public sealed partial class LitanyBlessableOddityCondition : LitanyCondition<LitanyBlessableOddityCondition>;

public sealed partial class LitanyBlessableOddityConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBlessableOddityCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBlessableOddityCondition> args)
    {
        var holder = args.SourceEnt ?? entity.Owner;
        if (!Litany.TryGetHeldOddity(holder, out var item, out var oddity))
        {
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-oddity");
            args.Result = false;
            return;
        }

        if (Litany.IsOddityBlessed(item))
        {
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-oddity-blessed");
            args.Result = false;
            return;
        }

        if (!oddity.giving.Values.Any(value => value != 0))
        {
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-effect");
            args.Result = false;
            return;
        }

        args.Result = true;
    }
}

/// <summary>The caster can see at least one active follower within the litany's sense range.</summary>
public sealed partial class LitanySeesFollowersCondition : LitanyCondition<LitanySeesFollowersCondition>;

public sealed partial class LitanySeesFollowersConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySeesFollowersCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySeesFollowersCondition> args)
    {
        var seer = args.SourceEnt ?? entity.Owner;
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.GetCastContext(args.SourceEnt) is { } ctx &&
            Litany.CollectVisibleActiveFollowers(seer, Litany.GetSenseRange(ctx.Litany)).Count > 0);
    }
}

/// <summary>
/// Subject is a toggleable holy door. Broken doors report the door-off reason; other
/// ineligible machines report no-target (Activate Door's two failures).
/// </summary>
public sealed partial class LitanyDoorToggleableCondition : LitanyCondition<LitanyDoorToggleableCondition>;

public sealed partial class LitanyDoorToggleableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyDoorToggleableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyDoorToggleableCondition> args)
    {
        if (Litany.CanToggleLitanyDoor(entity))
        {
            args.Result = true;
            return;
        }

        Litany.ReportValidationFailure(args.SourceEnt,
            Litany.IsHolyDoorBroken(entity) ? "oxyd-litany-door-off" : "oxyd-litany-no-target");
        args.Result = false;
    }
}

/// <summary>Subject is a NeoTheology altar (Baptismal Record).</summary>
public sealed partial class LitanyIsAltarCondition : LitanyCondition<LitanyIsAltarCondition>;

public sealed partial class LitanyIsAltarConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyIsAltarCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyIsAltarCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, Litany.IsLitanyAltar(entity));
    }
}

/// <summary>Subject is an Eye of the Protector that can host the armaments shop (validate probe).</summary>
public sealed partial class LitanyArmamentsEyeCondition : LitanyCondition<LitanyArmamentsEyeCondition>;

public sealed partial class LitanyArmamentsEyeConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyArmamentsEyeCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyArmamentsEyeCondition> args)
    {
        var passes = false;
        if (Litany.IsLitanyEye(entity))
        {
            var probe = new LitanyOpenArmamentsEvent(args.SourceEnt ?? entity.Owner, false, ValidateOnly: true);
            Litany.RaiseOn(entity.Owner, ref probe);
            passes = probe.Handled;
        }

        args.Result = Check(args.SourceEnt, args.Condition, passes);
    }
}

/// <summary>Subject is a bioreactor whose chamber door can toggle (validate probe).</summary>
public sealed partial class LitanyBioreactorChamberCondition : LitanyCondition<LitanyBioreactorChamberCondition>;

public sealed partial class LitanyBioreactorChamberConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBioreactorChamberCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBioreactorChamberCondition> args)
    {
        var passes = false;
        if (Litany.IsLitanyBioreactor(entity))
        {
            var probe = new LitanyToggleBioreactorChamberEvent(entity.Owner, false, ValidateOnly: true);
            Litany.RaiseOn(entity.Owner, ref probe);
            passes = probe.Handled;
        }

        args.Result = Check(args.SourceEnt, args.Condition, passes);
    }
}

/// <summary>Subject is a bioreactor whose chamber solution can pump (validate probe).</summary>
public sealed partial class LitanyBioreactorPumpCondition : LitanyCondition<LitanyBioreactorPumpCondition>;

public sealed partial class LitanyBioreactorPumpConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBioreactorPumpCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBioreactorPumpCondition> args)
    {
        var passes = false;
        if (Litany.IsLitanyBioreactor(entity))
        {
            var probe = new LitanyPumpBioreactorEvent(entity.Owner, false, ValidateOnly: true);
            Litany.RaiseOn(entity.Owner, ref probe);
            passes = probe.Handled;
        }

        args.Result = Check(args.SourceEnt, args.Condition, passes);
    }
}

/// <summary>The subject's cruciform hosts a usable uplink module (Bounty's validate probe).</summary>
public sealed partial class LitanyUplinkOpenCondition : LitanyCondition<LitanyUplinkOpenCondition>;

public sealed partial class LitanyUplinkOpenConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUplinkOpenCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUplinkOpenCondition> args)
    {
        var probe = new LitanyUplinkOpenEvent(entity.Owner, ValidateOnly: true, Handled: false);
        Litany.RaiseOn(entity.Owner, ref probe);
        if (!probe.Handled)
            Litany.ReportValidationFailure(args.SourceEnt, probe.Failure ?? "oxyd-litany-uplink-none");

        args.Result = probe.Handled;
    }
}

/// <summary>Subject has a growable plant in view (Accelerated Growth's validate probe).</summary>
public sealed partial class LitanyGrowthProbeCondition : LitanyCondition<LitanyGrowthProbeCondition>;

public sealed partial class LitanyGrowthProbeConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyGrowthProbeCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyGrowthProbeCondition> args)
    {
        // Multiplier and duration are irrelevant while validating: the server probe only
        // reports whether any plant in view could take the boost.
        var probe = new LitanyAcceleratedGrowthEvent(entity.Owner, 0f, TimeSpan.Zero, false, ValidateOnly: true);
        Litany.RaiseOn(entity.Owner, ref probe);
        args.Result = Check(args.SourceEnt, args.Condition, probe.Handled);
    }
}
