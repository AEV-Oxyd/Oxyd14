using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
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
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Runs a litany's declarative effect list and exposes the shared helpers the
/// <see cref="LitanyEffect"/> classes need. Server <c>LitanySystem</c> owns the
/// cast transaction (cost, cooldown, speech); this system owns the effect phase.
/// </summary>
public sealed partial class LitanyEffectSystem : EntitySystem
{
    /// <summary>Eris soul_hunger nutrition delta; Oxyd routes through SatiationSystem Hunger.</summary>
    public const float SoulHungerNutritionAmount = 100f;

    /// <summary>Eris cruciform sense view range; catalog range must match.</summary>
    public const float CruciformSenseRangeMeters = 7f;

    private static readonly ProtoId<NeoTheologyProfilePrototype> PreacherProfile = "OxydNtPreacher";
    private static readonly ProtoId<NeoTheologyProfilePrototype> InquisitorProfile = "OxydNtInquisitor";
    private static readonly ProtoId<SpeciesPrototype> HumanSpecies = "Human";

    /// <summary>How far from the target a NeoTheology altar still counts as "their altar".</summary>
    private const float AltarSearchRadius = 1.5f;

    [Dependency] private readonly SharedCruciformSystem _cruciform = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SatiationSystem _satiation = default!;
    [Dependency] private readonly SharedSkillSystem _skill = default!;
    [Dependency] private readonly SharedStationSystem _stations = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>Disconnected-fixture capture of private social litany notices.</summary>
    private readonly Dictionary<EntityUid, List<string>> _testingSocialNotices = new();

    public bool TryValidateEffects(
        EntityUid user,
        LitanyPrototype litany,
        out LocId? failure,
        IReadOnlyList<EntityUid>? targets = null)
    {
        var context = new LitanyEffectContext(user, litany, targets ?? Array.Empty<EntityUid>());
        foreach (var effect in litany.Effects)
        {
            if (!effect.CanApply(this, context, out failure))
                return false;
        }

        failure = null;
        return true;
    }

    public bool TryApplyEffects(
        EntityUid user,
        LitanyPrototype litany,
        IReadOnlyList<EntityUid>? targets = null)
    {
        var context = new LitanyEffectContext(user, litany, targets ?? Array.Empty<EntityUid>());
        foreach (var effect in litany.Effects)
        {
            if (!effect.Apply(this, context))
                return false;
        }

        return true;
    }

    public void TestingClearSocialNotices()
    {
        _testingSocialNotices.Clear();
    }

    public IReadOnlyList<string> TestingGetSocialNotices(EntityUid recipient)
    {
        if (_testingSocialNotices.TryGetValue(recipient, out var list))
            return list;

        return Array.Empty<string>();
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

    /// <summary>
    /// Eris <c>lock_door</c> (machinery.dm): toggles the bolt on a holy door and mirrors
    /// the state into <see cref="NeoTheologyDoorComponent.LitanyLocked"/>. Returns whether
    /// the bolt state changed.
    /// </summary>
    public bool TryToggleLitanyDoor(EntityUid door, EntityUid user)
    {
        if (!TryComp<NeoTheologyDoorComponent>(door, out var litanyDoor) ||
            !TryComp<DoorBoltComponent>(door, out var bolt))
            return false;

        var locked = !_doors.IsBolted(door, bolt);
        if (!_doors.TrySetBoltDown(new Entity<DoorBoltComponent>(door, bolt), locked, user))
            return false;

        litanyDoor.LitanyLocked = locked;
        Dirty(door, litanyDoor);
        return true;
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

    /// <summary>
    /// Applies or refreshes one unique skill buff per listed skill. <paramref name="sourceId"/>
    /// is the unique source: recasting from the same source refreshes it, never stacks.
    /// A zero duration means no expiry (skill-system default), not an already-expired buff.
    /// </summary>
    public bool TryApplySkillBuff(
        EntityUid target,
        string sourceId,
        Dictionary<ProtoId<SkillPrototype>, int> amounts,
        TimeSpan duration)
    {
        if (!CanReceiveSkillBuff(target) || !TryComp<MobSkillComponent>(target, out var skills))
            return false;

        TimeSpan? expires = duration > TimeSpan.Zero ? duration : null;
        foreach (var (skill, amount) in amounts)
        {
            _skill.SetUniqueBuff((target, skills), sourceId, amount, skill, expires);
        }

        return true;
    }

    /// <summary>
    /// Applies or refreshes one litany-keyed unique skill penalty (negative amount) with no
    /// expiry — Eris <c>changeStat</c> is permanent, unlike the timed buffs above.
    /// </summary>
    public bool TryApplySkillPenalty(EntityUid target, string sourceId, ProtoId<SkillPrototype> skill, int amount)
    {
        if (amount >= 0 || !CanReceiveSkillBuff(target) || !TryComp<MobSkillComponent>(target, out var skills))
            return false;

        _skill.SetUniqueBuff((target, skills), sourceId, amount, skill, expires: null);
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
    /// §5.2 v1 conversion restriction: only actual Human-species humanoids may be committed.
    /// Everything else is rejected before anything is consumed and keeps its implant state
    /// untouched — no species-specific gibbings or limb surgery.
    /// </summary>
    public bool IsEligibleHuman(EntityUid uid)
    {
        return TryComp<HumanoidProfileComponent>(uid, out var profile) && profile.Species == HumanSpecies;
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
        altar = EntityUid.Invalid;
        cruciform = EntityUid.Invalid;
        if (!TryComp(target, out TransformComponent? targetXform))
            return false;

        var altars = _lookup.GetEntitiesInRange<NeoTheologyAltarComponent>(targetXform.Coordinates, AltarSearchRadius);
        foreach (var candidate in altars.OrderBy(entry => entry.Owner))
        {
            var items = _lookup.GetEntitiesInRange<CruciformComponent>(Transform(candidate.Owner).Coordinates, candidate.Comp.Radius);
            foreach (var item in items.OrderBy(entry => entry.Owner))
            {
                if (!IsLooseNeverActivatedCruciform(item.Owner, item.Comp))
                    continue;

                altar = candidate.Owner;
                cruciform = item.Owner;
                return true;
            }
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
    /// deleting it, dropping the same entity at <paramref name="body"/>'s coordinates. Container
    /// removal raises ImplantRemovedEvent, which is what lets CruciformSystem detach the bearer.
    /// <c>ForceRemove</c> is never used — it deletes the implant, and the cruciform must survive
    /// for re-installation and Resurrection.
    /// </summary>
    public bool TryExtractInstalledCruciform(EntityUid body, EntityUid cruciform)
    {
        if (!TryComp<ImplantedComponent>(body, out var installed) ||
            !installed.ImplantContainer.Contains(cruciform))
            return false;

        if (!_containers.Remove(cruciform, installed.ImplantContainer, destination: Transform(body).Coordinates))
            return false;

        // A corpse must not keep a pending cast after losing its cruciform.
        if (TryComp<CruciformBearerComponent>(body, out var bearer))
        {
            bearer.PendingRequestId = null;
            Dirty(body, bearer);
        }

        return true;
    }

    /// <summary>Oddity held in the active hand — DivineBlessing blesses the caster's own oddity.</summary>
    public bool TryGetHeldOddity(EntityUid user, out OddityComponent oddity)
    {
        oddity = null!;
        if (!_hands.TryGetActiveItem(user, out var item) || item is not { } held)
            return false;
        if (!TryComp(held, out OddityComponent? comp))
            return false;

        oddity = comp;
        return true;
    }

    public static bool IsClergyProfile(ProtoId<NeoTheologyProfilePrototype> profile)
    {
        return profile == PreacherProfile || profile == InquisitorProfile;
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

        var actorMap = actorXform.MapID;
        if (actorMap == MapId.Nullspace)
            return results;

        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (query.MoveNext(out var body, out _, out var xform))
        {
            if (body == actor)
                continue;
            if (xform.MapID != actorMap)
                continue;
            if (!_cruciform.TryGetCruciform(body, out _, out _))
                continue;
            if (!_examine.InRangeUnOccluded(actor, body, range, predicate: null))
                continue;

            results.Add(body);
        }

        return results;
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
        if (!_testingSocialNotices.TryGetValue(recipient, out var list))
        {
            list = [];
            _testingSocialNotices[recipient] = list;
        }

        list.Add(message);
        _popup.PopupEntity(message, recipient, recipient, PopupType.MediumCaution);
    }
}
