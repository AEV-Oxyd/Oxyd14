using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Station;
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

    [Dependency] private readonly SharedCruciformSystem _cruciform = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SatiationSystem _satiation = default!;
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
