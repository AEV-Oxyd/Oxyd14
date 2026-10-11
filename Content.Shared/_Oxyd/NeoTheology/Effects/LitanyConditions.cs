using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.EntityConditions;
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
public sealed partial class LitanyActiveCruciformCondition : LitanyCondition<LitanyActiveCruciformCondition>;

public sealed partial class LitanyActiveCruciformConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyActiveCruciformCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyActiveCruciformCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetActiveCruciform(target, out _));
    }
}

/// <summary>First resolved target's active cruciform has no attached upgrade (InstallUpgrade's free-slot check).</summary>
public sealed partial class LitanyCruciformUpgradeFreeCondition : LitanyCondition<LitanyCruciformUpgradeFreeCondition>;

public sealed partial class LitanyCruciformUpgradeFreeConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyCruciformUpgradeFreeCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeFreeCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetActiveCruciform(target, out var cruciform) && cruciform.Upgrade is null);
    }
}

/// <summary>First resolved target's installed cruciform carries an attached upgrade (UninstallUpgrade).</summary>
public sealed partial class LitanyCruciformUpgradeInstalledCondition : LitanyCondition<LitanyCruciformUpgradeInstalledCondition>;

public sealed partial class LitanyCruciformUpgradeInstalledConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyCruciformUpgradeInstalledCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeInstalledCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetInstalledCruciform(target, out var cruciform) && cruciform.Upgrade is not null);
    }
}

/// <summary>First resolved target has an installed cruciform (UninstallUpgrade's gate; stock per-target variant needs CruciformBearer).</summary>
public sealed partial class LitanyFirstTargetInstalledCruciformCondition : LitanyCondition<LitanyFirstTargetInstalledCruciformCondition>;

public sealed partial class LitanyFirstTargetInstalledCruciformConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyFirstTargetInstalledCruciformCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyFirstTargetInstalledCruciformCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetInstalledCruciform(target, out _));
    }
}

/// <summary>First resolved target's cruciform profile differs from the barred one (Omission bars the Inquisitor).</summary>
public sealed partial class LitanyNotProfileCondition : LitanyCondition<LitanyNotProfileCondition>
{
    /// <summary>The profile the target's cruciform must not carry.</summary>
    [DataField]
    public ProtoId<NeoTheologyProfilePrototype> Profile;
}

public sealed partial class LitanyNotProfileConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyNotProfileCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNotProfileCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryGetActiveCruciform(target, out var cruciform) &&
                cruciform.Profile != args.Condition.Profile);
    }
}

/// <summary>Target does not carry the Godblood mutation.</summary>
public sealed partial class LitanyNoGodbloodCondition : LitanyCondition<LitanyNoGodbloodCondition>;

public sealed partial class LitanyNoGodbloodConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyNoGodbloodCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoGodbloodCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => !Litany.HasGodblood(target));
    }
}

/// <summary>First resolved target does not reject holy influence (no atheist mutation).</summary>
public sealed partial class LitanyHolyInfluenceAcceptedCondition : LitanyCondition<LitanyHolyInfluenceAcceptedCondition>;

public sealed partial class LitanyHolyInfluenceAcceptedConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyHolyInfluenceAcceptedCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHolyInfluenceAcceptedCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => !Litany.RejectsHolyInfluence(target));
    }
}

/// <summary>No resolved target rejects holy influence (Words of Purging refuses the whole rite).</summary>
public sealed partial class LitanyNoTargetRejectsHolyCondition : LitanyCondition<LitanyNoTargetRejectsHolyCondition>;

public sealed partial class LitanyNoTargetRejectsHolyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNoTargetRejectsHolyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoTargetRejectsHolyCondition> args)
    {
        var rejects = Litany.GetCastContext(args.SourceEnt)?.Targets.Any(Litany.RejectsHolyInfluence) ?? true;
        args.Result = Check(args.SourceEnt, args.Condition, !rejects);
    }
}

/// <summary>
/// Target lies buckled and positioned on a NeoTheology procedure altar. Reports the
/// posture/clothing reason the helper returns instead of a fixed <see cref="FailureId"/>.
/// </summary>
public sealed partial class LitanyProcedureAltarCondition : LitanyCondition<LitanyProcedureAltarCondition>
{
    /// <summary>Eris install requires the follower to be undressed; uninstall does not.</summary>
    [DataField]
    public bool Undressed;
}

public sealed partial class LitanyProcedureAltarConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyProcedureAltarCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyProcedureAltarCondition> args)
    {
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
    }
}

/// <summary>A loose cruciform-upgrade item rests on the procedure altar beside the target.</summary>
public sealed partial class LitanyAltarUpgradePresentCondition : LitanyCondition<LitanyAltarUpgradePresentCondition>;

public sealed partial class LitanyAltarUpgradePresentConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyAltarUpgradePresentCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyAltarUpgradePresentCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.TryFindAltarUpgrade(target, out _, out _));
    }
}

/// <summary>First resolved target is a living mob that can take a sanity delta (Revelation's Belief gain).</summary>
public sealed partial class LitanySanityCapableCondition : LitanyCondition<LitanySanityCapableCondition>;

public sealed partial class LitanySanityCapableConditionSystem
    : LitanyFirstTargetConditionSystem<LitanySanityCapableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySanityCapableCondition> args)
    {
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => Litany.IsAlive(target) && Litany.CanReceiveSanityDelta(target));
    }
}

/// <summary>Target is a living mob that can carry skill buffs and accepts holy influence.</summary>
public sealed partial class LitanySkillBuffRecipientCondition : LitanyCondition<LitanySkillBuffRecipientCondition>;

public sealed partial class LitanySkillBuffRecipientConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySkillBuffRecipientCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySkillBuffRecipientCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.CanReceiveSkillBuff(entity) && !Litany.RejectsHolyInfluence(entity));
    }
}

/// <summary>Target's hunger is below its maximum (SoulHunger's paired-injury gate; per-target — the rite is Self).</summary>
public sealed partial class LitanyHungerNotFullCondition : LitanyCondition<LitanyHungerNotFullCondition>;

public sealed partial class LitanyHungerNotFullConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyHungerNotFullCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHungerNotFullCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetHunger(entity, out _, out var hunger, out var maxHunger) && hunger < maxHunger);
    }
}

/// <summary>The litany's Pain effect does not bar this target (Atonement refuses Godblood).</summary>
public sealed partial class LitanyPainAllowedCondition : LitanyCondition<LitanyPainAllowedCondition>;

public sealed partial class LitanyPainAllowedConditionSystem
    : LitanyFirstTargetConditionSystem<LitanyPainAllowedCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyPainAllowedCondition> args)
    {
        var refuses = Litany.GetCastContext(args.SourceEnt)
            ?.Litany.Effects.OfType<LitanyPainEffect>().FirstOrDefault()?.RefuseGodblood ?? false;
        args.Result = CheckFirstTarget(args.SourceEnt, args.Condition,
            target => !refuses || !Litany.HasGodblood(target));
    }
}

/// <summary>The first resolved body validates for soul transfer (Reincarnation validate-only event).</summary>
public sealed partial class LitanyReincarnatableCondition : LitanyCondition<LitanyReincarnatableCondition>;

public sealed partial class LitanyReincarnatableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyReincarnatableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyReincarnatableCondition> args)
    {
        var handled = false;
        if (Litany.GetCastContext(args.SourceEnt)?.Targets.FirstOrDefault() is { } target)
        {
            var check = new LitanyReincarnationEvent(target, false, ValidateOnly: true);
            Litany.RaiseOn(target, ref check);
            handled = check.Handled;
        }

        args.Result = Check(args.SourceEnt, args.Condition, handled);
    }
}

/// <summary>The cast resolved at least one target (caster-side; broadcast-rite guard).</summary>
public sealed partial class LitanyHasTargetsCondition : LitanyCondition<LitanyHasTargetsCondition>;

public sealed partial class LitanyHasTargetsConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyHasTargetsCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHasTargetsCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            (Litany.GetCastContext(args.SourceEnt)?.Targets.Count ?? 0) > 0);
    }
}

/// <summary>The caster's uplink module answers a validate-only probe (Knowledge).</summary>
public sealed partial class LitanyUplinkReadyCondition : LitanyCondition<LitanyUplinkReadyCondition>;

public sealed partial class LitanyUplinkReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUplinkReadyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUplinkReadyCondition> args)
    {
        if (args.SourceEnt is not { } user)
        {
            args.Result = Check(args.SourceEnt, args.Condition, false);
            return;
        }

        var probe = new LitanyUplinkReportEvent(user, true, false);
        Litany.RaiseOn(user, ref probe);
        args.Result = Check(args.SourceEnt, args.Condition, probe.Handled);
    }
}

/// <summary>A NeoTheology forge among the resolved targets accepts a produce run.</summary>
public sealed partial class LitanyForgeReadyCondition : LitanyCondition<LitanyForgeReadyCondition>;

public sealed partial class LitanyForgeReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyForgeReadyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyForgeReadyCondition> args)
    {
        var ready = false;
        if (Litany.GetCastContext(args.SourceEnt) is { } context)
        {
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
        }

        args.Result = Check(args.SourceEnt, args.Condition, ready);
    }
}

/// <summary>The Eye of the Protector among the resolved targets accepts the litany's offering.</summary>
public sealed partial class LitanyOfferingReadyCondition : LitanyCondition<LitanyOfferingReadyCondition>;

public sealed partial class LitanyOfferingReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyOfferingReadyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyOfferingReadyCondition> args)
    {
        var ready = false;
        var offering = Litany.GetCastContext(args.SourceEnt)
            ?.Litany.Effects.OfType<LitanyOfferingEffect>().FirstOrDefault()?.Offering;
        if (offering is { } key && args.SourceEnt is { } user &&
            Litany.GetCastContext(user) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!Litany.IsLitanyEye(target))
                    continue;

                var ev = new LitanyOfferingEvent(user, key, false, true);
                Litany.RaiseOn(target, ref ev);
                if (ev.Handled)
                {
                    ready = true;
                    break;
                }
            }
        }

        args.Result = Check(args.SourceEnt, args.Condition, ready);
    }
}

/// <summary>A NeoTheology biogenerator is among the resolved targets.</summary>
public sealed partial class LitanyBiogeneratorPresentCondition : LitanyCondition<LitanyBiogeneratorPresentCondition>;

public sealed partial class LitanyBiogeneratorPresentConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyBiogeneratorPresentCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyBiogeneratorPresentCondition> args)
    {
        var found = Litany.GetCastContext(args.SourceEnt)?.Targets.Any(Litany.IsLitanyBiogenerator) ?? false;
        args.Result = Check(args.SourceEnt, args.Condition, found);
    }
}

/// <summary>A holy door among the resolved targets accepts the biomatter repair.</summary>
public sealed partial class LitanyRepairableDoorCondition : LitanyCondition<LitanyRepairableDoorCondition>;

public sealed partial class LitanyRepairableDoorConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyRepairableDoorCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyRepairableDoorCondition> args)
    {
        var ready = false;
        if (args.SourceEnt is { } user && Litany.GetCastContext(user) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!Litany.IsLitanyDoor(target))
                    continue;

                var repair = new LitanyRepairDoorEvent(target, user, false, true);
                Litany.RaiseOn(target, ref repair);
                if (repair.Handled)
                {
                    ready = true;
                    break;
                }
            }
        }

        args.Result = Check(args.SourceEnt, args.Condition, ready);
    }
}

/// <summary>A faced cloner/reader pair can start the soul-safe resurrection job.</summary>
public sealed partial class LitanyResurrectionReadyCondition : LitanyCondition<LitanyResurrectionReadyCondition>;

public sealed partial class LitanyResurrectionReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyResurrectionReadyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyResurrectionReadyCondition> args)
    {
        var ready = false;
        if (args.SourceEnt is { } user && Litany.GetCastContext(user) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!Litany.IsLitanyCloner(target) ||
                    !Litany.TryResolveResurrectionReader(target, context.Targets, out var reader))
                    continue;

                var start = new LitanyResurrectionEvent(target, reader, false, true, user);
                Litany.RaiseOn(target, ref start);
                if (start.Handled)
                {
                    ready = true;
                    break;
                }
            }
        }

        args.Result = Check(args.SourceEnt, args.Condition, ready);
    }
}

/// <summary>The deterministic first resolved target can host a scrying session for the caster.</summary>
public sealed partial class LitanyScryableCondition : LitanyCondition<LitanyScryableCondition>;

public sealed partial class LitanyScryableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyScryableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyScryableCondition> args)
    {
        var ready = false;
        if (args.SourceEnt is { } user &&
            Litany.GetCastContext(user) is { Targets.Count: > 0 } context)
        {
            var duration = context.Litany.EffectDuration > TimeSpan.Zero
                ? context.Litany.EffectDuration
                : TimeSpan.FromSeconds(30);
            var probe = new LitanyScryingEvent(user, context.Targets[0], duration, false, true);
            Litany.RaiseOn(context.Targets[0], ref probe);
            ready = probe.Handled;
        }

        args.Result = Check(args.SourceEnt, args.Condition, ready);
    }
}

/// <summary>The caster picked a blueprint the construction system can manifest right now.</summary>
public sealed partial class LitanyManifestationReadyCondition : LitanyCondition<LitanyManifestationReadyCondition>;

public sealed partial class LitanyManifestationReadyConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyManifestationReadyCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyManifestationReadyCondition> args)
    {
        // The pre-choice validation pass has no blueprint yet; it passes like the old CanApply.
        var blueprint = Litany.GetCastContext(args.SourceEnt)?.SelectedBlueprint;
        if (blueprint is not { } picked || args.SourceEnt is not { } user)
        {
            args.Result = blueprint is null;
            return;
        }

        var check = new LitanyManifestationEvent(user, picked, true, false);
        Litany.RaiseOn(user, ref check);
        if (!check.Handled && check.Failure is { } failure)
            Litany.ReportValidationFailure(user, failure);
        args.Result = check.Handled;
    }
}

/// <summary>A NeoTheology construction in front of the caster can be uprooted.</summary>
public sealed partial class LitanyUprootableCondition : LitanyCondition<LitanyUprootableCondition>;

public sealed partial class LitanyUprootableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyUprootableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyUprootableCondition> args)
    {
        if (args.SourceEnt is not { } user)
        {
            args.Result = false;
            return;
        }

        var check = new LitanyUprootEvent(user, true, false);
        Litany.RaiseOn(user, ref check);
        if (!check.Handled && check.Failure is { } failure)
            Litany.ReportValidationFailure(user, failure);
        args.Result = check.Handled;
    }
}
/// <summary>Caster can receive a skill buff (SearingRevelation's lone CanApply check).</summary>
public sealed partial class LitanyCanReceiveSkillBuffCondition : LitanyCondition<LitanyCanReceiveSkillBuffCondition>;

public sealed partial class LitanyCanReceiveSkillBuffConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyCanReceiveSkillBuffCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCanReceiveSkillBuffCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, Litany.CanReceiveSkillBuff(entity));
    }
}
