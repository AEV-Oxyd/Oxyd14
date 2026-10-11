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

/// <summary>Target has an installed cruciform that is also active.</summary>
public sealed partial class LitanyActiveCruciformCondition : LitanyCondition<LitanyActiveCruciformCondition>;

public sealed partial class LitanyActiveCruciformConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyActiveCruciformCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyActiveCruciformCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetActiveCruciform(entity, out _));
    }
}

/// <summary>Target's active cruciform has no attached upgrade (InstallUpgrade's free-slot check).</summary>
public sealed partial class LitanyCruciformUpgradeFreeCondition : LitanyCondition<LitanyCruciformUpgradeFreeCondition>;

public sealed partial class LitanyCruciformUpgradeFreeConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyCruciformUpgradeFreeCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeFreeCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetActiveCruciform(entity, out var cruciform) && cruciform.Upgrade is null);
    }
}

/// <summary>Target's installed cruciform carries an attached upgrade (UninstallUpgrade).</summary>
public sealed partial class LitanyCruciformUpgradeInstalledCondition : LitanyCondition<LitanyCruciformUpgradeInstalledCondition>;

public sealed partial class LitanyCruciformUpgradeInstalledConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyCruciformUpgradeInstalledCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyCruciformUpgradeInstalledCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetInstalledCruciform(entity, out var cruciform) && cruciform.Upgrade is not null);
    }
}

/// <summary>Target's cruciform profile differs from the barred one (Omission bars the Inquisitor).</summary>
public sealed partial class LitanyNotProfileCondition : LitanyCondition<LitanyNotProfileCondition>
{
    /// <summary>The profile the target's cruciform must not carry.</summary>
    [DataField]
    public ProtoId<NeoTheologyProfilePrototype> Profile;
}

public sealed partial class LitanyNotProfileConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNotProfileCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNotProfileCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryGetActiveCruciform(entity, out var cruciform) &&
            cruciform.Profile != args.Condition.Profile);
    }
}

/// <summary>Target does not carry the Godblood mutation.</summary>
public sealed partial class LitanyNoGodbloodCondition : LitanyCondition<LitanyNoGodbloodCondition>;

public sealed partial class LitanyNoGodbloodConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyNoGodbloodCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyNoGodbloodCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, !Litany.HasGodblood(entity));
    }
}

/// <summary>Target does not reject holy influence (no atheist mutation).</summary>
public sealed partial class LitanyHolyInfluenceAcceptedCondition : LitanyCondition<LitanyHolyInfluenceAcceptedCondition>;

public sealed partial class LitanyHolyInfluenceAcceptedConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanyHolyInfluenceAcceptedCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyHolyInfluenceAcceptedCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition, !Litany.RejectsHolyInfluence(entity));
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
        if (Litany.TryGetProcedureAltar(entity, args.Condition.Undressed, out _, out var failure))
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
    : LitanyConditionSystem<TransformComponent, LitanyAltarUpgradePresentCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyAltarUpgradePresentCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.TryFindAltarUpgrade(entity, out _, out _));
    }
}

/// <summary>Target is a living mob that can take a sanity delta (Revelation's Belief gain).</summary>
public sealed partial class LitanySanityCapableCondition : LitanyCondition<LitanySanityCapableCondition>;

public sealed partial class LitanySanityCapableConditionSystem
    : LitanyConditionSystem<TransformComponent, LitanySanityCapableCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanySanityCapableCondition> args)
    {
        args.Result = Check(args.SourceEnt, args.Condition,
            Litany.IsAlive(entity) && Litany.CanReceiveSanityDelta(entity));
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

/// <summary>Target's hunger is below its maximum (SoulHunger's paired-injury gate).</summary>
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
    : LitanyConditionSystem<TransformComponent, LitanyPainAllowedCondition>
{
    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<LitanyPainAllowedCondition> args)
    {
        var refuses = Litany.GetCastContext(args.SourceEnt)
            ?.Litany.Effects.OfType<LitanyPainEffect>().FirstOrDefault()?.RefuseGodblood ?? false;
        args.Result = Check(args.SourceEnt, args.Condition,
            !refuses || !Litany.HasGodblood(entity));
    }
}

/// <summary>The dead body validates for soul transfer (Reincarnation validate-only event).</summary>
public sealed partial class LitanyReincarnatableCondition : LitanyCondition<LitanyReincarnatableCondition>;

public sealed partial class LitanyReincarnatableConditionSystem
    : LitanyConditionSystem<MobStateComponent, LitanyReincarnatableCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<LitanyReincarnatableCondition> args)
    {
        var check = new LitanyReincarnationEvent(entity, false, ValidateOnly: true);
        Litany.RaiseOn(entity, ref check);
        args.Result = Check(args.SourceEnt, args.Condition, check.Handled);
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
