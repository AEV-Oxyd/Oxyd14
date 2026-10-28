using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Sanity;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.EntityConditions;
using Content.Shared.EntityEffects;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Station;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Helper service for litany effect/condition handlers plus the two entry points the cast
/// transaction calls: <see cref="TryValidateEffects"/> evaluates each effect's
/// <see cref="EntityEffect.Conditions"/> against the resolved targets, and
/// <see cref="TryApplyEffects"/> raises the effect list through the shared EntityEffects
/// pipeline once per resolved target (or once on the caster for cast-scoped effects).
/// </summary>
public sealed partial class LitanyEffectSystem : EntitySystem
{
    /// <summary>Eris soul_hunger nutrition delta; Oxyd routes through SatiationSystem Hunger.</summary>
    public const float SoulHungerNutritionAmount = 100f;

    /// <summary>Eris cruciform sense view range; catalog range must match.</summary>
    public const float CruciformSenseRangeMeters = 7f;


    [Dependency] private SharedCruciformSystem _cruciform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private SharedSkillSystem _skill = default!;
    [Dependency] private SharedStationSystem _stations = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedEntityEffectsSystem _entityEffects = default!;
    [Dependency] private SharedEntityConditionsSystem _conditions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    /// <summary>OXYD test-only: integration fixtures subscribe to verify notice delivery.</summary>
    public event Action<EntityUid, string>? SocialNotice;

    /// <summary>Admin-log a litany mutation: actor did <paramref name="action"/> to target.</summary>
    public void AdminLog(LogType type, LogImpact impact, EntityUid actor, string action, EntityUid? target = null)
    {
        if (target is { } other)
            _adminLogger.Add(type, impact, $"{ToPrettyString(actor):user} {action} {ToPrettyString(other):target}");
        else
            _adminLogger.Add(type, impact, $"{ToPrettyString(actor):user} {action}");
    }

    /// <summary>
    /// Stamps the cast inputs on the caster's <see cref="LitanyCastContextComponent"/> for the
    /// duration of one validate/apply pass. Handlers and conditions read it back through
    /// <see cref="GetCastContext"/>; the component is wiped when the pass ends so a stale
    /// context never outlives its cast.
    /// </summary>
    /// <remarks>
    /// Must be paired with <see cref="PopCastContext"/> on the same path. A nested cast
    /// re-stamps the fields, so pushes are depth-counted and the innermost pop only
    /// removes the component once the outermost pass also ends — the outer frame's
    /// stamped fields are still clobbered, so nested casts remain unsupported.
    /// </remarks>
    public LitanyCastContextComponent PushCastContext(
        EntityUid user,
        LitanyPrototype litany,
        IReadOnlyList<EntityUid>? targets = null,
        IReadOnlyList<string>? selectedTokens = null,
        string? selectedText = null,
        ProtoId<NeoTheologyProfilePrototype>? designation = null,
        ProtoId<NeoTheologyBlueprintPrototype>? selectedBlueprint = null,
        int ceremonyParticipants = 0)
    {
        var context = EnsureComp<LitanyCastContextComponent>(user);
        context.Litany = litany;
        context.Targets.Clear();
        if (targets != null)
            context.Targets.AddRange(targets);
        context.SelectedTokens = selectedTokens != null ? new List<string>(selectedTokens) : null;
        context.SelectedText = selectedText;
        context.Designation = designation;
        context.SelectedBlueprint = selectedBlueprint;
        context.CeremonyParticipants = ceremonyParticipants;
        if (context.CastDepth > 0)
            Log.Warning($"nested litany cast context on {ToPrettyString(user)}; inner cast clobbers the outer frame's stamped fields");
        context.CastDepth++;
        context.LastApplySucceeded = false;
        context.ApplyFailed = false;
        context.ValidationFailure = null;
        return context;
    }

    /// <summary>The cast context the caster is currently stamped with; null between casts.</summary>
    public LitanyCastContextComponent? GetCastContext(EntityUid? user)
    {
        return user is { } uid && TryComp(uid, out LitanyCastContextComponent? context) ? context : null;
    }

    /// <summary>
    /// Clears the stamped context once a validate/apply pass finishes. Only the matching
    /// outermost pop removes the component; a pop without a push logs and returns.
    /// </summary>
    public void PopCastContext(EntityUid user)
    {
        if (!TryComp(user, out LitanyCastContextComponent? context) || context.CastDepth <= 0)
        {
            Log.Warning($"litany cast context pop without a matching push on {ToPrettyString(user)}");
            return;
        }
        if (--context.CastDepth == 0)
            RemComp<LitanyCastContextComponent>(user);
    }

    /// <summary>Handler result channel: an effect handler reports whether its apply succeeded.</summary>
    public void ReportApplyResult(EntityUid? user, bool succeeded)
    {
        if (GetCastContext(user) is not { } context)
            return;

        context.LastApplySucceeded = succeeded;
        if (!succeeded)
            context.ApplyFailed = true;
    }

    /// <summary>Condition failure channel: records the first reason for the validate pass to return.</summary>
    public void ReportValidationFailure(EntityUid? user, LocId failure)
    {
        if (GetCastContext(user) is { } context)
            context.ValidationFailure ??= failure;
    }

    /// <summary>
    /// Pre-debit validation: every effect's <see cref="EntityEffect.Conditions"/> must pass on
    /// at least one eligible entity — the resolved targets for target-scoped effects, the
    /// caster for cast-scoped ones and whenever no target was resolved.
    /// </summary>
    public bool TryValidateEffects(
        EntityUid user,
        LitanyPrototype litany,
        out LocId? failure,
        IReadOnlyList<EntityUid>? targets = null,
        IReadOnlyList<string>? selectedTokens = null,
        string? selectedText = null,
        ProtoId<NeoTheologyProfilePrototype>? designation = null,
        ProtoId<NeoTheologyBlueprintPrototype>? selectedBlueprint = null,
        int ceremonyParticipants = 0)
    {
        var context = PushCastContext(user, litany, targets, selectedTokens, selectedText,
            designation, selectedBlueprint, ceremonyParticipants);

        try
        {
            foreach (var effect in litany.Effects)
            {
                // Conditions always see the entity the effect will be raised on: each resolved
                // target for per-target effects, the caster for cast-scoped ones — matching the
                // apply pass in TryApplyEffects.
                var eval = effect is ILitanyEffect { PerTarget: true }
                    ? context.Targets
                    : new List<EntityUid> { user };
                var valid = eval.Count == 0
                    ? _conditions.TryConditions(user, effect.Conditions, user)
                    : eval.Any(t => !TerminatingOrDeleted(t) &&
                                    _conditions.TryConditions(t, effect.Conditions, user));

                if (!valid)
                {
                    failure = context.ValidationFailure ?? "oxyd-litany-no-effect";
                    return false;
                }
            }

            failure = null;
            return true;
        }
        finally
        {
            PopCastContext(user);
        }
    }

    /// <summary>
    /// Applies the litany through the shared EntityEffects pipeline: once per resolved target
    /// for target-scoped effects, once on the caster for the rest. A handler that reports
    /// failure — or an effect whose conditions pass on no target — fails the whole apply so
    /// the caller can refund, keeping the cast atomic.
    /// </summary>
    public bool TryApplyEffects(
        EntityUid user,
        LitanyPrototype litany,
        IReadOnlyList<EntityUid>? targets = null,
        IReadOnlyList<string>? selectedTokens = null,
        string? selectedText = null,
        ProtoId<NeoTheologyProfilePrototype>? designation = null,
        ProtoId<NeoTheologyBlueprintPrototype>? selectedBlueprint = null,
        int ceremonyParticipants = 0)
    {
        var context = PushCastContext(user, litany, targets, selectedTokens, selectedText,
            designation, selectedBlueprint, ceremonyParticipants);

        try
        {
            var success = true;
            foreach (var effect in litany.Effects)
            {
                if (effect is ILitanyEffect { PerTarget: true })
                {
                    // No resolved target: the rite still completes — it just skips its
                    // payload (Eris ceremony semantics). A list whose targets all vanished
                    // mid-chant fails instead so the cast refunds.
                    if (context.Targets.Count == 0)
                        continue;

                    var applied = false;
                    foreach (var target in context.Targets)
                    {
                        if (TerminatingOrDeleted(target))
                            continue;

                        // Reset to failure before the raise: a raise that never reports —
                        // no handler ran — must not be mistaken for a successful apply.
                        context.LastApplySucceeded = false;
                        applied |= _entityEffects.TryApplyEffect(target, effect, 1f, user) &&
                                   context.LastApplySucceeded;
                    }

                    if (!applied || context.ApplyFailed)
                        success = false;
                }
                else
                {
                    context.LastApplySucceeded = false;
                    var raised = _entityEffects.TryApplyEffect(user, effect, 1f, user);
                    if (!raised || !context.LastApplySucceeded || context.ApplyFailed)
                        success = false;
                }
            }

            return success;
        }
        finally
        {
            PopCastContext(user);
        }
    }

    public bool IsAlive(EntityUid uid)
    {
        return _mobState.IsAlive(uid);
    }

    public bool CanReceiveDamage(EntityUid uid)
    {
        return HasComp<DamageableComponent>(uid) && !HasComp<GodmodeComponent>(uid);
    }

    /// <summary>
    /// True when <paramref name="uid"/> is a holy door the door litanies can act on:
    /// a NeoTheology door that also carries the shared bolt state.
    /// </summary>
    public bool IsLitanyDoor(EntityUid uid)
    {
        return HasComp<NeoTheologyDoorComponent>(uid) && HasComp<DoorBoltComponent>(uid);
    }

    /// <summary>True when <paramref name="uid"/> is a NeoTheology altar.</summary>
    public bool IsLitanyAltar(EntityUid uid)
    {
        return HasComp<NeoTheologyAltarComponent>(uid);
    }

    /// <summary>
    /// Eris <c>lock_door</c> (machinery.dm): toggles the bolt on a holy door and mirrors
    /// the state into <see cref="NeoTheologyDoorComponent.LitanyLocked"/>. Returns whether
    /// the bolt state changed.
    /// </summary>
    public bool CanToggleLitanyDoor(EntityUid door)
    {
        return IsLitanyDoor(door) && _power.IsPowered(door) && !IsHolyDoorBroken(door);
    }

    /// <summary>Eris broken holy door: damage at the breakage threshold refuses Activate Door.</summary>
    public bool IsHolyDoorBroken(EntityUid door)
    {
        if (TryComp<NeoTheologyDoorComponent>(door, out var litanyDoor) && litanyDoor.Broken)
            return true;

        return _damageable.IsAtLeastTotalDamage(door, NeoTheologyDoorComponent.BrokenAt);
    }

    public bool TryToggleLitanyDoor(EntityUid door, EntityUid user)
    {
        if (!CanToggleLitanyDoor(door) || !TryComp<NeoTheologyDoorComponent>(door, out var litanyDoor) ||
            !TryComp<DoorBoltComponent>(door, out var bolt))
            return false;

        var locked = !_doors.IsBolted(door, bolt);
        if (!_doors.TrySetBoltDown(new Entity<DoorBoltComponent>(door, bolt), locked, user))
            return false;

        litanyDoor.LitanyLocked = locked;
        return true;
    }

    /// <summary>
    /// True when <paramref name="uid"/> is a NeoTheology cloner — the machine the Resurrection
    /// litany starts a soul-safe cloning job on.
    /// </summary>
    public bool IsLitanyCloner(EntityUid uid)
    {
        return HasComp<CruciformClonerComponent>(uid);
    }

    /// <summary>
    /// True when <paramref name="uid"/> is a cruciform reader — the machine Resurrection reads
    /// its soul out of.
    /// </summary>
    public bool IsLitanyReader(EntityUid uid)
    {
        return HasComp<CruciformReaderComponent>(uid);
    }

    /// <summary>
    /// The reader a faced cloner grows from: its linked reader, a reader in the cast's
    /// targets, or the nearest reader within 1.5 m.
    /// </summary>
    public bool TryResolveResurrectionReader(
        EntityUid cloner,
        IReadOnlyList<EntityUid> targets,
        out EntityUid reader)
    {
        reader = EntityUid.Invalid;
        if (TryComp<CruciformClonerComponent>(cloner, out var linked) &&
            linked.Reader is { } recorded &&
            IsLitanyReader(recorded) &&
            !TerminatingOrDeleted(recorded))
        {
            reader = recorded;
            return true;
        }

        foreach (var target in targets)
        {
            if (!IsLitanyReader(target))
                continue;

            reader = target;
            return true;
        }

        var best = float.MaxValue;
        var origin = _xform.GetWorldPosition(cloner);
        foreach (var (uid, _) in _lookup.GetEntitiesInRange<CruciformReaderComponent>(Transform(cloner).Coordinates, 1.5f))
        {
            if (!IsLitanyReader(uid))
                continue;

            var distance = (_xform.GetWorldPosition(uid) - origin).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            reader = uid;
        }

        return reader.IsValid();
    }

    /// <summary>True when <paramref name="uid"/> is a NeoTheology forge the MakeCruciform litany drives.</summary>
    public bool IsLitanyForge(EntityUid uid)
    {
        return HasComp<CruciformForgeComponent>(uid);
    }

    /// <summary>True when <paramref name="uid"/> is a NeoTheology biogenerator the PowerBiogenerator litany drives.</summary>
    public bool IsLitanyBiogenerator(EntityUid uid)
    {
        return HasComp<BiogeneratorComponent>(uid);
    }

    /// <summary>True when <paramref name="uid"/> is a bioreactor — the machine the two bioreactor litanies drive.</summary>
    public bool IsLitanyBioreactor(EntityUid uid)
    {
        return HasComp<BioreactorComponent>(uid);
    }

    /// <summary>True when <paramref name="uid"/> is an Eye of the Protector — the machine the offering litanies bank observation on.</summary>
    public bool IsLitanyEye(EntityUid uid)
    {
        return HasComp<EyeOfTheProtectorComponent>(uid);
    }

    /// <summary>True when <paramref name="uid"/> is the armaments printer — the machine the OrderArmaments litany opens.</summary>
    public bool IsLitanyArmamentsPrinter(EntityUid uid)
    {
        return HasComp<ArmamentsPrinterComponent>(uid);
    }

    /// <summary>True when the target is a living mob that can carry skill buffs.</summary>
    public bool CanReceiveSkillBuff(EntityUid uid)
    {
        return _mobState.IsAlive(uid) && HasComp<MobSkillComponent>(uid);
    }

    /// <summary>True when the target has sanity to modify (Revelation's Belief gain).</summary>
    public bool CanReceiveSanityDelta(EntityUid uid)
    {
        return HasComp<SanityComponent>(uid);
    }

    /// <summary>Review: there is no such thing as a permanent buff — litany-granted stat
    /// changes always time out after fifteen minutes.</summary>
    public static readonly TimeSpan LitanyBuffDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Applies or refreshes one unique skill buff per listed skill. <paramref name="sourceId"/>
    /// is the unique source: recasting from the same source refreshes it, never stacks.
    /// A zero duration falls back to <see cref="LitanyBuffDuration"/>, not an already-expired buff.
    /// </summary>
    public bool TryApplySkillBuff(
        EntityUid target,
        string sourceId,
        Dictionary<ProtoId<SkillPrototype>, int> amounts,
        TimeSpan duration)
    {
        if (!CanReceiveSkillBuff(target) || !TryComp<MobSkillComponent>(target, out var skills))
            return false;

        var expires = duration > TimeSpan.Zero ? duration : LitanyBuffDuration;
        foreach (var (skill, amount) in amounts)
        {
            _skill.SetUniqueBuff((target, skills), sourceId, amount, skill, expires);
        }

        return true;
    }

    /// <summary>
    /// Applies or refreshes one litany-keyed timed skill change. <paramref name="sourceId"/>
    /// bounds stacking exactly like <see cref="TryApplySkillBuff"/>: recasting from the same
    /// source refreshes the buff instead of piling on another.
    /// </summary>
    public void TryAddTimedSkill(EntityUid target, string sourceId, ProtoId<SkillPrototype> skill, int amount)
    {
        if (!CanReceiveSkillBuff(target) || !TryComp<MobSkillComponent>(target, out var skills))
            return;

        _skill.SetUniqueBuff((target, skills), sourceId, amount, skill, LitanyBuffDuration);
    }

    /// <summary>
    /// Applies or refreshes one litany-keyed unique skill penalty (negative amount) that times
    /// out after <see cref="LitanyBuffDuration"/> like every other buff.
    /// </summary>
    public bool TryApplySkillPenalty(EntityUid target, string sourceId, ProtoId<SkillPrototype> skill, int amount)
    {
        if (amount >= 0 || !CanReceiveSkillBuff(target) || !TryComp<MobSkillComponent>(target, out var skills))
            return false;

        _skill.SetUniqueBuff((target, skills), sourceId, amount, skill, LitanyBuffDuration);
        return true;
    }

    /// <summary>
    /// Shared damage entry point. Healing litanies pass negative values, SoulHunger
    /// passes the positive injury. Returns false when nothing was applied.
    /// </summary>
    public bool TryApplyDamage(EntityUid target, DamageSpecifier damage)
    {
        return _damageable.TryChangeDamage(
            target,
            damage,
            ignoreResistances: true,
            interruptsDoAfters: false,
            origin: target,
            ignoreGlobalModifiers: true);
    }

    /// <summary>
    /// True when a heal entry's key names a damage group ("Brute") rather than a damage
    /// type ("Blunt"). The engine only applies type entries; group entries must be
    /// spread over the group's present damage via <see cref="TryHealDamageGroup"/>.
    /// Group membership wins on the rare id that is registered as both (test-only
    /// prototypes can add a type that collides with a production group).
    /// </summary>
    public bool IsDamageGroup(ProtoId<DamageTypePrototype> type)
    {
        return ProtoMan.HasIndex<DamageGroupPrototype>(type.Id);
    }

    /// <summary>
    /// Heals a damage-group entry from a litany heal block: the negative budget is spread
    /// over the group's present positive damage (engine <c>HealDistributed</c>), so
    /// "Brute: -20" heals 20 total across Blunt/Slash/Piercing, never 20 per subtype.
    /// </summary>
    public bool TryHealDamageGroup(EntityUid target, ProtoId<DamageTypePrototype> group, FixedPoint2 budget)
    {
        if (budget >= FixedPoint2.Zero || !CanReceiveDamage(target))
            return false;

        return !_damageable.HealDistributed(target, budget, group.Id).Empty;
    }

    public bool TryGetHunger(
        EntityUid uid,
        out Entity<SatiationComponent> satiation,
        out float hunger,
        out float maxHunger)
    {
        satiation = default;
        hunger = 0f;
        maxHunger = 0f;

        if (!TryComp(uid, out SatiationComponent? comp))
            return false;

        var ent = new Entity<SatiationComponent>(uid, comp);
        if (_satiation.GetValueOrNull(ent, SatiationSystem.Hunger) is not { } value ||
            _satiation.GetMaximumValue(ent, SatiationSystem.Hunger) is not { } max)
            return false;

        satiation = ent;
        hunger = value;
        maxHunger = max;
        return true;
    }

    public void AddHunger(Entity<SatiationComponent> satiation, float amount)
    {
        _satiation.ModifyValue(satiation, SatiationSystem.Hunger, amount);
    }

    public bool TryGetActiveCruciform(EntityUid body, out CruciformComponent component)
    {
        return _cruciform.TryGetCruciform(body, out _, out component);
    }

    /// <summary>Installed cruciform regardless of active state (Epiphany activates it).</summary>
    public bool TryGetInstalledCruciform(EntityUid body, out CruciformComponent component)
    {
        return _cruciform.TryGetCruciformEntity(body, out _, out component);
    }

    /// <summary>Installed cruciform entity plus state; the extraction path needs the entity itself.</summary>
    public bool TryGetInstalledCruciformEntity(EntityUid body, out EntityUid cruciform, out CruciformComponent component)
    {
        return _cruciform.TryGetCruciformEntity(body, out cruciform, out component);
    }

    /// <summary>True only for MobState.Dead — Critical is still a living target.</summary>
    public bool IsDead(EntityUid uid)
    {
        return _mobState.IsDead(uid);
    }

    /// <summary>
    /// §5.2 v1 conversion restriction: the effect declares the required species as data.
    /// Everything else is rejected before anything is consumed and keeps its implant state
    /// untouched — no species-specific gibbings or limb surgery.
    /// </summary>
    public bool SpeciesMatches(EntityUid uid, ProtoId<SpeciesPrototype> species)
    {
        return TryComp<HumanoidProfileComponent>(uid, out var profile) && profile.Species == species;
    }

    /// <summary>
    /// Finds the loose, never-activated cruciform resting on a NeoTheology altar beside
    /// <paramref name="target"/> — Eris install's <c>get_front(user)</c> item lookup adapted to
    /// the altar's turf radius. Candidates are uid-sorted, so an unchanged world yields the same
    /// altar/item pair at begin and commit; commit re-runs this lookup instead of picking a
    /// different pair mid-chant.
    /// </summary>
    public bool TryFindAltarCruciform(EntityUid target, out EntityUid altar, out EntityUid cruciform)
    {
        cruciform = EntityUid.Invalid;
        if (!TryGetProcedureAltar(target, true, out altar, out _))
            return false;

        var items = _lookup.GetEntitiesInRange<CruciformComponent>(Transform(altar).Coordinates,
            Comp<NeoTheologyAltarComponent>(altar).Radius);
        foreach (var item in items.OrderBy(entry => entry.Owner))
        {
            if (!IsLooseNeverActivatedCruciform(item.Owner, item.Comp))
                continue;

            cruciform = item.Owner;
            return true;
        }

        return false;
    }

    /// <summary>An implant the altar ritual may install: loose (in no container) and never activated.</summary>
    private bool IsLooseNeverActivatedCruciform(EntityUid uid, CruciformComponent component)
    {
        return !component.EverActivated &&
               !component.Active &&
               !_containers.IsEntityInContainer(uid) &&
               TryComp<SubdermalImplantComponent>(uid, out var implant) &&
               implant.ImplantedEntity == null;
    }

    /// <summary>
    /// Finds the loose cruciform-upgrade item resting on the NeoTheology altar beside
    /// <paramref name="target"/> — the attachment counterpart of
    /// <see cref="TryFindAltarCruciform"/>. Candidates are uid-sorted, so an unchanged world
    /// yields the same altar/item pair at begin and commit; an installed attachment lives inside
    /// the cruciform's container and is deliberately skipped.
    /// </summary>
    public bool TryFindAltarUpgrade(EntityUid target, out EntityUid altar, out EntityUid upgradeItem)
    {
        upgradeItem = EntityUid.Invalid;
        if (!TryGetProcedureAltar(target, true, out altar, out _))
            return false;

        var items = _lookup.GetEntitiesInRange<CruciformUpgradeComponent>(Transform(altar).Coordinates,
            Comp<NeoTheologyAltarComponent>(altar).Radius);
        foreach (var item in items.OrderBy(entry => entry.Owner))
        {
            if (_containers.IsEntityInContainer(item.Owner))
                continue;

            upgradeItem = item.Owner;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Eris install(): insert the existing loose cruciform with <c>ForceImplant</c>, then confirm
    /// the bearer linkage actually resolved. A rejected insert (duplicate guard) fails loudly
    /// here instead of silently no-opping.
    /// </summary>
    public bool TryImplantLooseCruciform(EntityUid target, EntityUid cruciform)
    {
        if (!TryComp<SubdermalImplantComponent>(cruciform, out var implant))
            return false;

        _implants.ForceImplant(target, (cruciform, implant));

        return _cruciform.TryGetCruciformEntity(target, out var linked, out _) && linked == cruciform;
    }

    /// <summary>
    /// Eris ejection(): remove the installed cruciform from the implant container WITHOUT
    /// deleting it, dropping the same entity beside <paramref name="body"/> so the corpse cannot hide it. Container
    /// removal raises ImplantRemovedEvent, which is what lets CruciformSystem detach the bearer.
    /// <c>ForceRemove</c> is never used — it deletes the implant, and the cruciform must survive
    /// for re-installation and Resurrection.
    /// </summary>
    public bool TryExtractInstalledCruciform(EntityUid body, EntityUid cruciform)
    {
        if (!TryComp<ImplantedComponent>(body, out var installed) ||
            !installed.ImplantContainer.Contains(cruciform))
            return false;

        var dropCoordinates = Transform(body).Coordinates.Offset(Vector2.UnitX);
        if (!_containers.Remove(cruciform, installed.ImplantContainer, destination: dropCoordinates))
            return false;

        // A corpse must not keep a pending cast after losing its cruciform.
        if (TryComp<CruciformBearerComponent>(body, out var bearer))
        {
            bearer.PendingRequestId = null;
            Dirty(body, bearer);
        }

        return true;
    }

    /// <summary>Divine Blessing prefers the active oddity, or the sole oddity held beside the Bible.</summary>
    public bool IsOddityBlessed(EntityUid item) => HasComp<CruciformBlessedComponent>(item);

    public void MarkOddityBlessed(EntityUid item) => EnsureComp<CruciformBlessedComponent>(item);

    public bool TryGetHeldOddity(EntityUid user, out EntityUid item, out OddityComponent oddity)
    {
        item = EntityUid.Invalid;
        oddity = null!;
        if (_hands.TryGetActiveItem(user, out var active) && active is { } held &&
            TryComp<OddityComponent>(held, out var activeOddity))
        {
            item = held;
            oddity = activeOddity;
            return true;
        }

        if (active is not { } book || !HasComp<LitanyBookComponent>(book) ||
            !TryComp<Content.Shared.Hands.Components.HandsComponent>(user, out var hands))
            return false;

        foreach (var candidate in _hands.EnumerateHeld((user, hands)))
        {
            if (!TryComp<OddityComponent>(candidate, out var heldOddity))
                continue;
            if (item != EntityUid.Invalid)
                return false; // Ambiguous: never bless a different item arbitrarily.
            item = candidate;
            oddity = heldOddity;
        }

        return item != EntityUid.Invalid;
    }

    public bool IsClergyProfile(ProtoId<NeoTheologyProfilePrototype> profile)
    {
        return _prototypes.TryIndex(profile, out NeoTheologyProfilePrototype? proto) &&
               proto.Clearance >= NeoTheologyClearance.Clergy;
    }

    public float GetSenseRange(LitanyPrototype litany)
    {
        return litany.Range > 0 ? litany.Range : CruciformSenseRangeMeters;
    }

    public bool Prob(float chance)
    {
        return _random.Prob(chance);
    }

    /// <summary>Inclusive integer roll from the shared random (Revelation 0..10, blessing 1..8).</summary>
    public int RollInclusive(int min, int max)
    {
        return _random.Next(min, max + 1);
    }

    /// <summary>
    /// Raises a by-ref event on a target on behalf of an effect. Effects are prototype data
    /// with no bus access; server-only handlers own the authoritative side (sanity delta,
    /// cruciform activation) and set <c>Handled</c>.
    /// The raise is a local broadcast: a bridge target need not carry any component the
    /// handler could subscribe on (Adoption's non-believer has no bearer component at all).
    /// Component-scoped subscribers are still reached through the regular directed dispatch.
    /// </summary>
    public void RaiseOn<TEvent>(EntityUid target, ref TEvent args) where TEvent : notnull
    {
        RaiseLocalEvent(target, ref args, broadcast: true);
    }

    public string GetName(EntityUid uid, EntityUid? viewer = null)
    {
        return Identity.Name(uid, EntityManager, viewer);
    }

    public List<EntityUid> CollectVisibleActiveFollowers(EntityUid actor, float range)
    {
        var results = new List<EntityUid>();
        if (!TryComp(actor, out TransformComponent? actorXform))
            return results;

        if (actorXform.MapID == MapId.Nullspace)
            return results;

        // Spatial lookup first: only nearby bearers pay the LOS raycast cost.
        foreach (var body in _lookup.GetEntitiesInRange<CruciformBearerComponent>(actorXform.Coordinates, range))
        {
            if (body.Owner == actor || TerminatingOrDeleted(body))
                continue;
            if (!_cruciform.TryGetCruciform(body, out _, out _))
                continue;
            if (!_examine.InRangeUnOccluded(actor, body, range, predicate: null))
                continue;

            results.Add(body);
        }

        return results;
    }

    /// <summary>
    /// Wrapper so effects can test an optional bearer/entity whitelist without a direct dep.
    /// A null whitelist accepts every entity.
    /// </summary>
    public bool IsWhitelisted(EntityWhitelist? whitelist, EntityUid uid)
    {
        return whitelist == null || _whitelist.IsValid(whitelist, uid);
    }

    public IEnumerable<EntityUid> EnumerateGlobalActiveFollowers(EntityUid actor)
    {
        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out var xform))
            if (body != actor && xform.MapID != MapId.Nullspace &&
                !TerminatingOrDeleted(body) && !EntityManager.IsQueuedForDeletion(body) &&
                _cruciform.TryGetCruciform(body, out _, out _))
                yield return body;
    }

    public IEnumerable<EntityUid> EnumerateSameStationActiveFollowers(EntityUid actor)
    {
        var actorStation = _stations.GetOwningStation(actor);
        if (!TryComp(actor, out TransformComponent? actorXform))
            yield break;

        var actorMap = actorXform.MapID;
        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out var xform))
        {
            if (body == actor)
                continue;
            if (!_cruciform.TryGetCruciform(body, out _, out _))
                continue;

            if (actorStation is { } station)
            {
                if (_stations.GetOwningStation(body) != station)
                    continue;
            }
            else
            {
                // Plan §7.1: no station → same map only; never link null stations across maps.
                if (xform.MapID != actorMap || actorMap == MapId.Nullspace)
                    continue;
            }

            yield return body;
        }
    }

    public string DescribeLocation(EntityUid actor)
    {
        var xform = Transform(actor);
        var mapCoords = _xform.ToMapCoordinates(xform.Coordinates);
        var point = $"{mapCoords.X:F0}, {mapCoords.Y:F0}";

        if (_stations.GetOwningStation(actor) is { } station)
            return $"{Name(station)} ({point})";

        if (xform.GridUid is { } grid)
            return $"{Name(grid)} ({point})";

        return $"({point})";
    }

    public void DeliverSocialNotice(EntityUid recipient, string message)
    {
        SocialNotice?.Invoke(recipient, message);
        _popup.PopupEntity(message, recipient, recipient, PopupType.MediumCaution);
    }
}
