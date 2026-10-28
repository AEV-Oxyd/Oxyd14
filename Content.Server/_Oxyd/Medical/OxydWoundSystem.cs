using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Damage.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris's external-organ wound model (modules/organs + modules/surgery): incoming brute
/// damage is attributed to external organ entities, which track fractures, bleeding incisions,
/// embedded items and a per-organ damage pool used by surgery and organ-repair chems.
/// </summary>
public sealed partial class OxydWoundSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private float _updateRemaining;
    private const float UpdateInterval = 2f;

    /// <summary>Brute delta at or above this can fracture an unarmoured external organ.</summary>
    private const float FractureThreshold = 12f;
    private const float FractureChance = 0.5f;

    private static readonly ProtoId<OrganCategoryPrototype>[] ExternalCategories =
    {
        "Torso", "Head", "ArmLeft", "ArmRight", "HandLeft", "HandRight",
        "LegLeft", "LegRight", "FootLeft", "FootRight",
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, DamageDealtEvent>(OnBodyDamaged);
    }

    public static bool IsExternal(OrganComponent organ) =>
        organ.Category is { } cat && ExternalCategories.Contains(cat.Id);

    /// <summary>All organs of a body together with their surgery state (lazily ensured).</summary>
    public List<(EntityUid Uid, OrganComponent Organ, OxydOrganSurgeryComponent Surgery)> GetOrgans(EntityUid body)
    {
        var list = new List<(EntityUid, OrganComponent, OxydOrganSurgeryComponent)>();
        var query = EntityQueryEnumerator<OrganComponent>();
        while (query.MoveNext(out var uid, out var organ))
        {
            if (organ.Body != body)
                continue;
            var surg = EnsureComp<OxydOrganSurgeryComponent>(uid);
            list.Add((uid, organ, surg));
        }
        return list;
    }

    /// <summary>
    /// Eris get_pulse(): dead - PULSE_NONE, critical - PULSE_THREADY, a heart organ
    /// damaged past a fifth of its cap - irregular, otherwise PULSE_NORM.
    /// </summary>
    public OxydPulse ClassifyPulse(EntityUid body, bool critical, bool dead)
    {
        if (dead)
            return OxydPulse.None;
        if (critical)
            return OxydPulse.Thready;
        foreach (var (_, organ, surg) in GetOrgans(body))
        {
            if (organ.Category is { } cat && cat.Id == OxydOrganCategories.Heart &&
                surg.OrganDamage >= OxydOrganSurgeryComponent.OrganMaxDamage * 0.2f)
                return OxydPulse.Irregular;
        }
        return OxydPulse.Norm;
    }

    private void OnBodyDamaged(EntityUid uid, BodyComponent comp, DamageDealtEvent args)
    {
        if (_mobs.IsDead(uid))
            return;

        var delta = args.Damage;
        var brute = OxydDamageTypes.Sum(delta, OxydDamageTypes.Brute);
        var burn = OxydDamageTypes.Sum(delta, OxydDamageTypes.Burn);
        var incoming = brute + burn * 0.5f;
        if (incoming < 3f)
            return;

        var externals = GetOrgans(uid).Where(o => IsExternal(o.Organ)).ToList();
        if (externals.Count == 0)
            return;

        var (orgUid, organ, surg) = _random.Pick(externals);
        surg.BruteDamage += brute * 0.35f;
        surg.BurnDamage += burn * 0.5f * 0.35f;
        surg.OrganDamage = surg.BruteDamage + surg.BurnDamage;

        if (brute >= FractureThreshold && !surg.Fractured && _random.Prob(FractureChance))
        {
            surg.Fractured = true;
            surg.Splinted = false;
            _popup.PopupEntity(Loc.GetString("oxyd-medical-fracture",
                ("organ", Name(orgUid))), uid, uid);
        }

        // Damage pools and Fractured are networked fields: sync every mutation, not just fractures.
        Dirty(orgUid, surg);
    }

    public override void Update(float frameTime)
    {
        _updateRemaining -= frameTime;
        if (_updateRemaining > 0)
            return;
        _updateRemaining = UpdateInterval;

        var query = EntityQueryEnumerator<OxydOrganSurgeryComponent>();
        while (query.MoveNext(out var uid, out var surg))
        {
            if (!TryComp<OrganComponent>(uid, out var organ) || organ.Body is not { } body)
                continue;

            var dirty = false;

            // An open, unclamped incision bleeds into the body's bleed pool (Eris wound bleeding).
            var incisionBleed = surg is { Incision: not OxydIncisionStage.None, Clamped: false }
                ? 0.6f
                : 0f;
            if (Math.Abs(surg.WoundBleedRate - incisionBleed) > 0.01f)
            {
                surg.WoundBleedRate = incisionBleed;
                dirty = true;
            }

            if (incisionBleed > 0)
                _bloodstream.TryModifyBleedAmount((body, null), incisionBleed * UpdateInterval);

            // Fractures hurt and keep the organ from regenerating (Eris is_broken pain + healing halt).
            if (surg.Fractured && TryComp<PainComponent>(body, out var pain) && _mobs.IsAlive(body))
                pain.TemporaryPain = Math.Min(pain.TemporaryPain + 0.4f * UpdateInterval, 30f);

            // Organ damage slowly recovers once the wound is sealed and set.
            if (surg is { OrganDamage: > 0, Incision: OxydIncisionStage.None, Fractured: false })
            {
                var heal = Math.Min(surg.OrganDamage, 0.15f * UpdateInterval);
                ReduceOrganDamage(surg, heal);
                dirty = true;
            }

            if (dirty)
                Dirty(uid, surg);
        }
    }

    /// <summary>Marks every organ of the body diagnosed (medical scanner / full scan, Eris autodiagnose).</summary>
    public void DiagnoseAll(EntityUid body)
    {
        foreach (var (orgUid, _, surg) in GetOrgans(body))
        {
            if (surg.Diagnosed)
                continue;
            surg.Diagnosed = true;
            Dirty(orgUid, surg);
        }
    }

    /// <summary>Heals per-organ damage (Eris chems like peridaxon, surgical repair).</summary>
    public void HealOrganDamage(EntityUid body, float amount)
    {
        foreach (var (orgUid, _, surg) in GetOrgans(body).Where(o => o.Surgery.OrganDamage > 0)
                     .OrderByDescending(o => o.Surgery.OrganDamage).Take(2))
        {
            ReduceOrganDamage(surg, amount);
            Dirty(orgUid, surg);
        }
    }

    /// <summary>Heals organ damage proportionally across the brute/burn pools.</summary>
    public static void ReduceOrganDamage(OxydOrganSurgeryComponent surg, float amount)
    {
        if (surg.OrganDamage <= 0)
            return;
        var frac = Math.Clamp(amount / surg.OrganDamage, 0f, 1f);
        surg.BruteDamage = Math.Max(0, surg.BruteDamage - surg.BruteDamage * frac);
        surg.BurnDamage = Math.Max(0, surg.BurnDamage - surg.BurnDamage * frac);
        surg.OrganDamage = surg.BruteDamage + surg.BurnDamage;
    }

    /// <summary>Adds organ damage into a specific pool (brute or burn).</summary>
    public static void AddOrganDamage(OxydOrganSurgeryComponent surg, float amount, bool burn = false)
    {
        if (burn)
            surg.BurnDamage = Math.Max(0, surg.BurnDamage + amount);
        else
            surg.BruteDamage = Math.Max(0, surg.BruteDamage + amount);
        surg.OrganDamage = surg.BruteDamage + surg.BurnDamage;
    }

    /// <summary>Mends a random fractured organ (Eris ossisine behaviour).</summary>
    public bool MendRandomFracture(EntityUid body)
    {
        var fractured = GetOrgans(body).Where(o => o.Surgery.Fractured).ToList();
        if (fractured.Count == 0)
            return false;

        var (orgUid, _, surg) = _random.Pick(fractured);
        surg.Fractured = false;
        surg.Splinted = false;
        Dirty(orgUid, surg);
        if (TryComp<PainComponent>(body, out var pain))
            pain.TemporaryPain += 15f;
        _popup.PopupEntity(Loc.GetString("oxyd-medical-bone-mended"), body, body);
        return true;
    }
}
