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
using Content.Shared._Oxyd.NeoTheology.Prototypes;
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

/// <summary>Base for conditions that apply the old CanApply's target[0] check to the first resolved target.</summary>
public abstract partial class LitanyFirstTargetConditionSystem<TCon>
    : LitanyConditionSystem<TransformComponent, TCon>
    where TCon : LitanyCondition<TCon>
{
    protected bool CheckFirstTarget(EntityUid? sourceEnt, TCon condition, Func<EntityUid, bool> predicate)
    {
        var target = Litany.GetCastContext(sourceEnt)?.Targets.FirstOrDefault();
        return Check(sourceEnt, condition, target is { } uid && predicate(uid));
    }
}
/// <summary>First resolved target has an installed cruciform that is also active.</summary>
public sealed partial class LitanyFirstTargetActiveCruciformCondition : LitanyCondition<LitanyFirstTargetActiveCruciformCondition>;
public sealed partial class LitanyActiveCruciformConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyActiveCruciformCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyActiveCruciformCondition> args)
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetActiveCruciform(target, out _));
/// <summary>First resolved target's active cruciform has no attached upgrade (InstallUpgrade's free-slot check).</summary>
public sealed partial class LitanyCruciformUpgradeFreeCondition : LitanyCondition<LitanyCruciformUpgradeFreeCondition>;
public sealed partial class LitanyCruciformUpgradeFreeConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyCruciformUpgradeFreeCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeFreeCondition> args)
            target => Litany.TryGetActiveCruciform(target, out var cruciform) && cruciform.Upgrade is null);
/// <summary>First resolved target's installed cruciform carries an attached upgrade (UninstallUpgrade).</summary>
public sealed partial class LitanyCruciformUpgradeInstalledCondition : LitanyCondition<LitanyCruciformUpgradeInstalledCondition>;
public sealed partial class LitanyCruciformUpgradeInstalledConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyCruciformUpgradeInstalledCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeInstalledCondition> args)
            target => Litany.TryGetInstalledCruciform(target, out var cruciform) && cruciform.Upgrade is not null);
/// <summary>First resolved target has an installed cruciform (UninstallUpgrade's gate; stock per-target variant needs CruciformBearer).</summary>
public sealed partial class LitanyFirstTargetInstalledCruciformCondition : LitanyCondition<LitanyFirstTargetInstalledCruciformCondition>;
public sealed partial class LitanyFirstTargetInstalledCruciformConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyFirstTargetInstalledCruciformCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyFirstTargetInstalledCruciformCondition> args)
            target => Litany.TryGetInstalledCruciform(target, out _));
/// <summary>First resolved target's cruciform profile differs from the barred one (Omission bars the Inquisitor).</summary>
public sealed partial class LitanyFirstTargetNotProfileCondition : LitanyCondition<LitanyFirstTargetNotProfileCondition>
    /// <summary>The profile the target's cruciform must not carry.</summary>
    [DataField]
    public ProtoId<NeoTheologyProfilePrototype> Profile;
public sealed partial class LitanyNotProfileConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyNotProfileCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNotProfileCondition> args)
            target => Litany.TryGetActiveCruciform(target, out var cruciform) &&
                cruciform.Profile != args.Condition.Profile);
/// <summary>Target does not carry the Godblood mutation.</summary>
public sealed partial class LitanyFirstTargetNoGodbloodCondition : LitanyCondition<LitanyFirstTargetNoGodbloodCondition>;
public sealed partial class LitanyNoGodbloodConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyNoGodbloodCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoGodbloodCondition> args)
            target => !Litany.HasGodblood(target));
/// <summary>First resolved target does not reject holy influence (no atheist mutation).</summary>
public sealed partial class LitanyHolyInfluenceAcceptedCondition : LitanyCondition<LitanyHolyInfluenceAcceptedCondition>;
public sealed partial class LitanyHolyInfluenceAcceptedConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyHolyInfluenceAcceptedCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHolyInfluenceAcceptedCondition> args)
            target => !Litany.RejectsHolyInfluence(target));
/// <summary>No resolved target rejects holy influence (Words of Purging refuses the whole rite).</summary>
public sealed partial class LitanyNoTargetRejectsHolyCondition : LitanyCondition<LitanyNoTargetRejectsHolyCondition>;
public sealed partial class LitanyNoTargetRejectsHolyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNoTargetRejectsHolyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoTargetRejectsHolyCondition> args)
        var rejects = Litany.GetCastContext(args.SourceEnt)?.Targets.Any(Litany.RejectsHolyInfluence) ?? true;
        args.Result = Check(args.SourceEnt, args.Condition, !rejects);
/// <summary>
/// Target lies buckled and positioned on a NeoTheology procedure altar. Reports the
/// posture/clothing reason the helper returns instead of a fixed <see cref="FailureId"/>.
/// </summary>
public sealed partial class LitanyFirstTargetProcedureAltarCondition : LitanyCondition<LitanyFirstTargetProcedureAltarCondition>
    /// <summary>Eris install requires the follower to be undressed; uninstall does not.</summary>
    public bool Undressed;
public sealed partial class LitanyProcedureAltarConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyProcedureAltarCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyProcedureAltarCondition> args)
        var target = Litany.GetCastContext(args.SourceEnt)?.Targets.FirstOrDefault();
        if (target is { } uid &&
            Litany.TryGetProcedureAltar(uid, args.Condition.Undressed, out _, out var failure))
        {
            args.Result = true;
            return;
        }
        if (failure is { } reason)
            Litany.ReportValidationFailure(args.SourceEnt, reason);
        args.Result = false;
/// <summary>A loose cruciform-upgrade item rests on the procedure altar beside the target.</summary>
public sealed partial class LitanyAltarUpgradePresentCondition : LitanyCondition<LitanyAltarUpgradePresentCondition>;
public sealed partial class LitanyAltarUpgradePresentConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyAltarUpgradePresentCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyAltarUpgradePresentCondition> args)
            target => Litany.TryFindAltarUpgrade(target, out _, out _));
/// <summary>First resolved target is a living mob that can take a sanity delta (Revelation's Belief gain).</summary>
public sealed partial class LitanySanityCapableCondition : LitanyCondition<LitanySanityCapableCondition>;
public sealed partial class LitanySanityCapableConditionSystem
    : LitanyFirstTargetConditionSystem<LitanySanityCapableCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySanityCapableCondition> args)
            target => Litany.IsAlive(target) && Litany.CanReceiveSanityDelta(target));
/// <summary>Target is a living mob that can carry skill buffs and accepts holy influence.</summary>
public sealed partial class LitanySkillBuffRecipientCondition : LitanyCondition<LitanySkillBuffRecipientCondition>;
public sealed partial class LitanySkillBuffRecipientConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySkillBuffRecipientCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySkillBuffRecipientCondition> args)
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.CanReceiveSkillBuff(entity) && !Litany.RejectsHolyInfluence(entity));
/// <summary>Target's hunger is below its maximum (SoulHunger's paired-injury gate; per-target — the rite is Self).</summary>
public sealed partial class LitanyHungerNotFullCondition : LitanyCondition<LitanyHungerNotFullCondition>;
public sealed partial class LitanyHungerNotFullConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyHungerNotFullCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHungerNotFullCondition> args)
            Litany.TryGetHunger(entity, out _, out var hunger, out var maxHunger) && hunger < maxHunger);
/// <summary>The litany's Pain effect does not bar this target (Atonement refuses Godblood).</summary>
public sealed partial class LitanyPainAllowedCondition : LitanyCondition<LitanyPainAllowedCondition>;
public sealed partial class LitanyPainAllowedConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyPainAllowedCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyPainAllowedCondition> args)
        var refuses = Litany.GetCastContext(args.SourceEnt)
            ?.Litany.Effects.OfType<LitanyPainEffect>().FirstOrDefault()?.RefuseGodblood ?? false;
            target => !refuses || !Litany.HasGodblood(target));
/// <summary>The first resolved body validates for soul transfer (Reincarnation validate-only event).</summary>
public sealed partial class LitanyReincarnatableCondition : LitanyCondition<LitanyReincarnatableCondition>;
public sealed partial class LitanyReincarnatableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyReincarnatableCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyReincarnatableCondition> args)
        var handled = false;
        if (Litany.GetCastContext(args.SourceEnt)?.Targets.FirstOrDefault() is { } target)
            var check = new LitanyReincarnationEvent(target, false, ValidateOnly: true);
            Litany.RaiseOn(target, ref check);
            handled = check.Handled;
        args.Result = Check(args.SourceEnt, args.Condition, handled);
/// <summary>The cast resolved at least one target (caster-side; broadcast-rite guard).</summary>
public sealed partial class LitanyHasTargetsCondition : LitanyCondition<LitanyHasTargetsCondition>;
public sealed partial class LitanyHasTargetsConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyHasTargetsCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHasTargetsCondition> args)
            (Litany.GetCastContext(args.SourceEnt)?.Targets.Count ?? 0) > 0);
/// <summary>The caster's uplink module answers a validate-only probe (Knowledge).</summary>
public sealed partial class LitanyUplinkReadyCondition : LitanyCondition<LitanyUplinkReadyCondition>;
public sealed partial class LitanyUplinkReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUplinkReadyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUplinkReadyCondition> args)
        if (args.SourceEnt is not { } user)
            args.Result = Check(args.SourceEnt, args.Condition, false);
        var probe = new LitanyUplinkReportEvent(user, true, false);
        Litany.RaiseOn(user, ref probe);
        args.Result = Check(args.SourceEnt, args.Condition, probe.Handled);
/// <summary>A NeoTheology forge among the resolved targets accepts a produce run.</summary>
public sealed partial class LitanyForgeReadyCondition : LitanyCondition<LitanyForgeReadyCondition>;
public sealed partial class LitanyForgeReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyForgeReadyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyForgeReadyCondition> args)
        var ready = false;
        if (Litany.GetCastContext(args.SourceEnt) is { } context)
            foreach (var target in context.Targets)
            {
                if (!Litany.IsLitanyForge(target))
                    continue;
                var produce = new LitanyForgeProduceEvent(target, false, true);
                Litany.RaiseOn(target, ref produce);
                if (produce.Handled)
                {
                    ready = true;
                    break;
                }
            }
        args.Result = Check(args.SourceEnt, args.Condition, ready);
/// <summary>The Eye of the Protector among the resolved targets accepts the litany's offering.</summary>
public sealed partial class LitanyOfferingReadyCondition : LitanyCondition<LitanyOfferingReadyCondition>;
public sealed partial class LitanyOfferingReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyOfferingReadyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyOfferingReadyCondition> args)
        var offering = Litany.GetCastContext(args.SourceEnt)
            ?.Litany.Effects.OfType<LitanyOfferingEffect>().FirstOrDefault()?.Offering;
        if (offering is { } key && args.SourceEnt is { } user &&
            Litany.GetCastContext(user) is { } context)
                if (!Litany.IsLitanyEye(target))
                var ev = new LitanyOfferingEvent(user, key, false, true);
                Litany.RaiseOn(target, ref ev);
                if (ev.Handled)
/// <summary>A NeoTheology biogenerator is among the resolved targets.</summary>
public sealed partial class LitanyBiogeneratorPresentCondition : LitanyCondition<LitanyBiogeneratorPresentCondition>;
public sealed partial class LitanyBiogeneratorPresentConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBiogeneratorPresentCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBiogeneratorPresentCondition> args)
        var found = Litany.GetCastContext(args.SourceEnt)?.Targets.Any(Litany.IsLitanyBiogenerator) ?? false;
        args.Result = Check(args.SourceEnt, args.Condition, found);
/// <summary>A holy door among the resolved targets accepts the biomatter repair.</summary>
public sealed partial class LitanyRepairableDoorCondition : LitanyCondition<LitanyRepairableDoorCondition>;
public sealed partial class LitanyRepairableDoorConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyRepairableDoorCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyRepairableDoorCondition> args)
        if (args.SourceEnt is { } user && Litany.GetCastContext(user) is { } context)
                if (!Litany.IsLitanyDoor(target))
                var repair = new LitanyRepairDoorEvent(target, user, false, true);
                Litany.RaiseOn(target, ref repair);
                if (repair.Handled)
/// <summary>A faced cloner/reader pair can start the soul-safe resurrection job.</summary>
public sealed partial class LitanyResurrectionReadyCondition : LitanyCondition<LitanyResurrectionReadyCondition>;
public sealed partial class LitanyResurrectionReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyResurrectionReadyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyResurrectionReadyCondition> args)
                if (!Litany.IsLitanyCloner(target) ||
                    !Litany.TryResolveResurrectionReader(target, context.Targets, out var reader))
                var start = new LitanyResurrectionEvent(target, reader, false, true, user);
                Litany.RaiseOn(target, ref start);
                if (start.Handled)
/// <summary>The deterministic first resolved target can host a scrying session for the caster.</summary>
public sealed partial class LitanyScryableCondition : LitanyCondition<LitanyScryableCondition>;
public sealed partial class LitanyScryableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyScryableCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyScryableCondition> args)
        if (args.SourceEnt is { } user &&
            Litany.GetCastContext(user) is { Targets.Count: > 0 } context)
            var duration = context.Litany.EffectDuration > TimeSpan.Zero
                ? context.Litany.EffectDuration
                : TimeSpan.FromSeconds(30);
            var probe = new LitanyScryingEvent(user, context.Targets[0], duration, false, true);
            Litany.RaiseOn(context.Targets[0], ref probe);
            ready = probe.Handled;
/// <summary>The caster picked a blueprint the construction system can manifest right now.</summary>
public sealed partial class LitanyManifestationReadyCondition : LitanyCondition<LitanyManifestationReadyCondition>;
public sealed partial class LitanyManifestationReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyManifestationReadyCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyManifestationReadyCondition> args)
        // The pre-choice validation pass has no blueprint yet; it passes like the old CanApply.
        var blueprint = Litany.GetCastContext(args.SourceEnt)?.SelectedBlueprint;
        if (blueprint is not { } picked || args.SourceEnt is not { } user)
            args.Result = blueprint is null;
        var check = new LitanyManifestationEvent(user, picked, true, false);
        Litany.RaiseOn(user, ref check);
        if (!check.Handled && check.Failure is { } failure)
            Litany.ReportValidationFailure(user, failure);
        args.Result = check.Handled;
/// <summary>A NeoTheology construction in front of the caster can be uprooted.</summary>
public sealed partial class LitanyUprootableCondition : LitanyCondition<LitanyUprootableCondition>;
public sealed partial class LitanyUprootableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUprootableCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUprootableCondition> args)
            args.Result = false;
        var check = new LitanyUprootEvent(user, true, false);
/// <summary>Caster can receive a skill buff (SearingRevelation's lone CanApply check).</summary>
public sealed partial class LitanyFirstTargetCanReceiveSkillBuffCondition : LitanyCondition<LitanyFirstTargetCanReceiveSkillBuffCondition>;
public sealed partial class LitanyCanReceiveSkillBuffConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyCanReceiveSkillBuffCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCanReceiveSkillBuffCondition> args)
        args.Result = Check(args.SourceEnt, args.Condition, Litany.CanReceiveSkillBuff(entity));
/// <summary>Target bears an installed, ACTIVE cruciform (Adoption/Confirmation/Excommunication/Initiation gates).</summary>
    : LitanyConditionSystem<MobStateComponent, LitanyActiveCruciformCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyActiveCruciformCondition> args)
            Litany.TryGetActiveCruciform(entity, out _));
/// Target bears an installed cruciform that has NOT activated yet (Epiphany's baptism
/// candidate). A body with no implant reports no-cruciform; an already-active one reports
/// already-active, matching the Eris gate order.
public sealed partial class LitanyInactiveCruciformCondition : LitanyCondition<LitanyInactiveCruciformCondition>;
public sealed partial class LitanyInactiveCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyInactiveCruciformCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyInactiveCruciformCondition> args)
        if (!Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform))
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-cruciform");
        if (cruciform.Active)
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-already-active");
        args.Result = true;
/// <summary>Target has no installed cruciform at all (Commitment refuses a second implant).</summary>
public sealed partial class LitanyNoInstalledCruciformCondition : LitanyCondition<LitanyNoInstalledCruciformCondition>;
public sealed partial class LitanyNoInstalledCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyNoInstalledCruciformCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNoInstalledCruciformCondition> args)
            !Litany.TryGetInstalledCruciformEntity(entity, out _, out _));
/// <summary>Target's installed cruciform has at least one removable core upgrade (Asacris).</summary>
public sealed partial class LitanyHasCoreUpgradesCondition : LitanyCondition<LitanyHasCoreUpgradesCondition>;
public sealed partial class LitanyHasCoreUpgradesConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyHasCoreUpgradesCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyHasCoreUpgradesCondition> args)
            Litany.TryGetInstalledCruciformEntity(entity, out _, out var cruciform) &&
            cruciform.CoreUpgrades.Count > 0);
/// <summary>Target is the given species (Commitment's §5.2 v1 conversion restriction).</summary>
public sealed partial class LitanySpeciesCondition : LitanyCondition<LitanySpeciesCondition>
    /// <summary>The only eligible species; populated by the owning effect after deserialization.</summary>
    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species;
public sealed partial class LitanySpeciesConditionSystem
    : LitanyConditionSystem<HumanoidProfileComponent, LitanySpeciesCondition>
    protected override void Condition(Entity<HumanoidProfileComponent> entity, ref EntityConditionEvent<LitanySpeciesCondition> args)
            Litany.SpeciesMatches(entity, args.Condition.Species));
/// <summary>Target is buckled, lying down on a NeoTheology altar (optionally undressed).</summary>
    /// <summary>Require the patient to carry no clothing (Commitment's install).</summary>
    public bool Undressed = true;
    : LitanyConditionSystem<MobStateComponent, LitanyProcedureAltarCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyProcedureAltarCondition> args)
        var passes = Litany.TryGetProcedureAltar(entity, args.Condition.Undressed, out _, out var failure);
        if (!passes && failure is { } reason)
        args.Result = passes;
/// <summary>A loose, never-activated cruciform rests on the procedure altar beside the target.</summary>
public sealed partial class LitanyAltarCruciformCondition : LitanyCondition<LitanyAltarCruciformCondition>;
public sealed partial class LitanyAltarCruciformConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyAltarCruciformCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyAltarCruciformCondition> args)
            Litany.TryFindAltarCruciform(entity, out _, out _));
/// <summary>Target's installed cruciform profile differs from the given one (Excommunication's inquisitor veto).</summary>
    /// <summary>The refused profile.</summary>
    : LitanyConditionSystem<MobStateComponent, LitanyNotProfileCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNotProfileCondition> args)
            cruciform.Profile != args.Condition.Profile);
/// <summary>Target's cruciform carries neither the priest nor the inquisitor rank module.</summary>
public sealed partial class LitanyNotPreacherCondition : LitanyCondition<LitanyNotPreacherCondition>;
public sealed partial class LitanyNotPreacherConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyNotPreacherCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyNotPreacherCondition> args)
            !cruciform.InstalledModules.Contains(NeoTheologyPrototypes.PriestModule) &&
            !cruciform.InstalledModules.Contains(NeoTheologyPrototypes.InquisitorModule));
/// <summary>Target's installed cruciform carries the given core module (Initiation's ascension kit).</summary>
public sealed partial class LitanyCruciformModuleCondition : LitanyCondition<LitanyCruciformModuleCondition>
    /// <summary>The required installed module.</summary>
    public ProtoId<CoreModulePrototype> Module;
public sealed partial class LitanyCruciformModuleConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyCruciformModuleCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyCruciformModuleCondition> args)
            cruciform.InstalledModules.Contains(args.Condition.Module));
/// <summary>Target does not carry the godblood mutation (Excommunication refuses Godblood bearers).</summary>
    : LitanyConditionSystem<TransformComponent, LitanyNoGodbloodCondition>
            !HasComp<GodbloodMutationComponent>(entity));
/// <summary>Target does not carry the atheist mutation (reagents rejected by biological rejection).</summary>
public sealed partial class LitanyNotAtheistCondition : LitanyCondition<LitanyNotAtheistCondition>;
public sealed partial class LitanyNotAtheistConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNotAtheistCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNotAtheistCondition> args)
            !HasComp<AtheistMutationComponent>(entity));
/// <summary>Target is a live mob that can carry skill buffs (Call to Battle).</summary>
    : LitanyConditionSystem<MobSkillComponent, LitanyCanReceiveSkillBuffCondition>
    protected override void Condition(Entity<MobSkillComponent> entity, ref EntityConditionEvent<LitanyCanReceiveSkillBuffCondition> args)
/// <summary>Subject can take a chemical dose of the given total volume (alive, bloodstream, capacity).</summary>
public sealed partial class LitanyCanInjectCondition : LitanyCondition<LitanyCanInjectCondition>
    /// <summary>The combined dose volume the bloodstream must accept.</summary>
    public FixedPoint2 MinVolume;
public sealed partial class LitanyCanInjectConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyCanInjectCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyCanInjectCondition> args)
            Litany.CanInjectMedicine(entity, args.Condition.MinVolume));
/// The caster holds a blessable oddity: held item, not already blessed, and at least one
/// non-zero skill in its giving table (DivineBlessing's three checks in Eris order).
public sealed partial class LitanyBlessableOddityCondition : LitanyCondition<LitanyBlessableOddityCondition>;
public sealed partial class LitanyBlessableOddityConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBlessableOddityCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBlessableOddityCondition> args)
        var holder = args.SourceEnt ?? entity.Owner;
        if (!Litany.TryGetHeldOddity(holder, out var item, out var oddity))
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-oddity");
        if (Litany.IsOddityBlessed(item))
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-oddity-blessed");
        if (!oddity.giving.Values.Any(value => value != 0))
            Litany.ReportValidationFailure(args.SourceEnt, "oxyd-litany-no-effect");
/// <summary>The caster can see at least one active follower within the litany's sense range.</summary>
public sealed partial class LitanySeesFollowersCondition : LitanyCondition<LitanySeesFollowersCondition>;
public sealed partial class LitanySeesFollowersConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySeesFollowersCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySeesFollowersCondition> args)
        var seer = args.SourceEnt ?? entity.Owner;
            Litany.GetCastContext(args.SourceEnt) is { } ctx &&
            Litany.CollectVisibleActiveFollowers(seer, Litany.GetSenseRange(ctx.Litany)).Count > 0);
/// Subject is a toggleable holy door. Broken doors report the door-off reason; other
/// ineligible machines report no-target (Activate Door's two failures).
public sealed partial class LitanyDoorToggleableCondition : LitanyCondition<LitanyDoorToggleableCondition>;
public sealed partial class LitanyDoorToggleableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyDoorToggleableCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyDoorToggleableCondition> args)
        if (Litany.CanToggleLitanyDoor(entity))
        Litany.ReportValidationFailure(args.SourceEnt,
            Litany.IsHolyDoorBroken(entity) ? "oxyd-litany-door-off" : "oxyd-litany-no-target");
/// <summary>Subject is a NeoTheology altar (Baptismal Record).</summary>
public sealed partial class LitanyIsAltarCondition : LitanyCondition<LitanyIsAltarCondition>;
public sealed partial class LitanyIsAltarConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyIsAltarCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyIsAltarCondition> args)
        args.Result = Check(args.SourceEnt, args.Condition, Litany.IsLitanyAltar(entity));
/// <summary>Subject is an Eye of the Protector that can host the armaments shop (validate probe).</summary>
public sealed partial class LitanyArmamentsEyeCondition : LitanyCondition<LitanyArmamentsEyeCondition>;
public sealed partial class LitanyArmamentsEyeConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyArmamentsEyeCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyArmamentsEyeCondition> args)
        var passes = false;
        if (Litany.IsLitanyEye(entity))
            var probe = new LitanyOpenArmamentsEvent(args.SourceEnt ?? entity.Owner, false, ValidateOnly: true);
            Litany.RaiseOn(entity.Owner, ref probe);
            passes = probe.Handled;
        args.Result = Check(args.SourceEnt, args.Condition, passes);
/// <summary>Subject is a bioreactor whose chamber door can toggle (validate probe).</summary>
public sealed partial class LitanyBioreactorChamberCondition : LitanyCondition<LitanyBioreactorChamberCondition>;
public sealed partial class LitanyBioreactorChamberConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBioreactorChamberCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBioreactorChamberCondition> args)
        if (Litany.IsLitanyBioreactor(entity))
            var probe = new LitanyToggleBioreactorChamberEvent(entity.Owner, false, ValidateOnly: true);
/// <summary>Subject is a bioreactor whose chamber solution can pump (validate probe).</summary>
public sealed partial class LitanyBioreactorPumpCondition : LitanyCondition<LitanyBioreactorPumpCondition>;
public sealed partial class LitanyBioreactorPumpConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBioreactorPumpCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBioreactorPumpCondition> args)
            var probe = new LitanyPumpBioreactorEvent(entity.Owner, false, ValidateOnly: true);
/// <summary>The subject's cruciform hosts a usable uplink module (Bounty's validate probe).</summary>
public sealed partial class LitanyUplinkOpenCondition : LitanyCondition<LitanyUplinkOpenCondition>;
public sealed partial class LitanyUplinkOpenConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUplinkOpenCondition>
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUplinkOpenCondition> args)
        var probe = new LitanyUplinkOpenEvent(entity.Owner, ValidateOnly: true, Handled: false);
        Litany.RaiseOn(entity.Owner, ref probe);
        if (!probe.Handled)
            Litany.ReportValidationFailure(args.SourceEnt, probe.Failure ?? "oxyd-litany-uplink-none");
        args.Result = probe.Handled;
/// <summary>Subject has a growable plant in view (Accelerated Growth's validate probe).</summary>
public sealed partial class LitanyGrowthProbeCondition : LitanyCondition<LitanyGrowthProbeCondition>;
public sealed partial class LitanyGrowthProbeConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyGrowthProbeCondition>
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyGrowthProbeCondition> args)
        // Multiplier and duration are irrelevant while validating: the server probe only
        // reports whether any plant in view could take the boost.
        var probe = new LitanyAcceleratedGrowthEvent(entity.Owner, 0f, TimeSpan.Zero, false, ValidateOnly: true);
