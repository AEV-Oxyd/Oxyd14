using Content.Server.Chat.Systems;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared._Oxyd.Skills;
using Content.Shared.StatusEffectNew;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// P3.2: the Eye of the Protector banks observation from active faithful in its radius.
/// <see cref="Update"/> only paces the scan; the work lives in <see cref="Scan"/> so tests drive it
/// without waiting out <see cref="EyeOfTheProtectorComponent.ScanInterval"/>.
/// </summary>
public sealed class EyeOfTheProtectorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly SharedSkillSystem _skill = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeOfTheProtectorComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
    }

    /// <summary>P3.7: push a read-only status snapshot when the Eye's UI is opened.</summary>
    private void OnUiOpened(EntityUid uid, EyeOfTheProtectorComponent component, AfterActivatableUIOpenEvent args)
    {
        var cooldown = component.NextMiracle - _timing.CurTime;
        if (cooldown < TimeSpan.Zero)
            cooldown = TimeSpan.Zero;

        _ui.SetUiState(uid, EyeOfTheProtectorUiKey.Key, new EyeOfTheProtectorState(
            component.Observation,
            component.ArmamentsPoints,
            component.MaxArmamentsPoints,
            cooldown));
    }

    /// <summary>When each Eye next scans.</summary>
    /// <remarks>ponytail: entries for deleted Eyes are never pruned. One Eye per station per the Eris
    /// map, so the leak is bounded; prune with an EntityTerminating handler if that changes.</remarks>
    private readonly Dictionary<EntityUid, TimeSpan> _nextScan = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out var uid, out var eye))
        {
            TryMiracle(uid, eye); // self-gates on NextMiracle; runs even when the scan tick is skipped

            if (_nextScan.TryGetValue(uid, out var next) && now < next)
                continue;

            _nextScan[uid] = now + eye.ScanInterval;
            eye.Scanned.Clear(); // new scan window: bearers can be re-awarded
            Scan(uid, eye);
        }
    }

    public void AddObservation(EntityUid eye, float amount)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp))
            return;

        comp.Observation = Math.Clamp(comp.Observation + amount, 0f, comp.MaxObservation);
        Dirty(eye, comp);
    }

    /// <summary>One scan: award each active faithful in radius exactly once per window.</summary>
    /// <remarks>
    /// ponytail: Eris also penalises mutants (<c>mutation_index</c>) and carrion (<c>is_carrion</c>)
    /// here via ObservationPerFaithless. Neither marker exists in this fork (only Botany plant
    /// mutations), so the penalty is deferred until a real marker lands — do not map it onto a
    /// guessed stand-in. ObservationPerFaithless stays unused until then.
    /// </remarks>
    public void Scan(EntityUid eye, EyeOfTheProtectorComponent? comp = null)
    {
        if (!Resolve(eye, ref comp))
            return;

        var xform = Transform(eye);
        var bearers = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (bearers.MoveNext(out var body, out var bearer, out var bodyXform))
        {
            if (bearer.Cruciform is not { } cruciform ||
                !TryComp<CruciformComponent>(cruciform, out var state) ||
                !state.Active ||
                bodyXform.MapID != xform.MapID)
                continue;

            if ((bodyXform.WorldPosition - xform.WorldPosition).Length() > comp.ObservationRadius)
                continue;

            if (!comp.Scanned.Add(body))
                continue;

            AddObservation(eye, comp.ObservationPerFaithful);
            _statusEffects.TryAddStatusEffectDuration(body, "OxydNtEyeBlessing", comp.FaithfulBlessingDuration);
        }

        // P3.5: accrue armament points from the observation bank each scan. This diverges from Eris
        // (which adds a fixed +125 per miracle, not observation/100 per scan) — flagged, not silent.
        comp.ArmamentsPoints = Math.Min(comp.MaxArmamentsPoints, comp.ArmamentsPoints + (int)(comp.Observation / 100f));
        Dirty(eye, comp);
    }

    /// <summary>The first Eye on the same map as <paramref name="near"/>, if any.</summary>
    /// <remarks>ponytail: deterministic order not required — one Eye per station.</remarks>
    public EntityUid? FindEye(EntityUid near)
    {
        var map = Transform(near).MapID;
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                return uid;
        }

        return null;
    }

    /// <summary>Debit <paramref name="cost"/> armament points, or refuse if the bank is short.</summary>
    public bool TrySpendArmaments(EntityUid eye, int cost)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp))
            return false;

        if (comp.ArmamentsPoints < cost)
            return false;

        comp.ArmamentsPoints -= cost;
        Dirty(eye, comp);
        return true;
    }

    private static readonly ProtoId<SkillPrototype>[] MiracleSkills =
    {
        "Rob", "Vig", "Tgh", "Cog", "Mec", "Bio"
    };

    private static readonly EntProtoId[] MiracleMaterials =
    {
        "SheetPlasteel", "SheetPlasma", "SheetUranium", "IngotGold", "IngotSilver", "MaterialDiamond"
    };

    /// <summary>
    /// P3.6: fire a miracle when the observation bank can fund one. Gated on <c>NextMiracle</c>
    /// (cadence) and a 1000-point observation cost, mirroring Eris <c>power_release()</c>.
    /// </summary>
    public void TryMiracle(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        if (_timing.CurTime < comp.NextMiracle)
            return;

        comp.NextMiracle = _timing.CurTime + comp.MiracleInterval;

        if (comp.Observation < 1000f)
            return;

        comp.Observation -= 1000f;
        FireRandomMiracle(eye, comp);
    }

    private void FireRandomMiracle(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        var xform = Transform(eye);

        switch (_random.Next(6))
        {
            case 0: // ALERT
                _chat.DispatchStationAnnouncement(eye, Loc.GetString("oxyd-eotp-miracle"));
                break;

            case 1: // INSPIRATION — insight amount is a flagged balance choice.
                foreach (var (body, _) in FaithfulInRange(eye, comp))
                {
                    if (TryComp<SanityComponent>(body, out var sanity))
                        _sanity.GiveInsight((body, sanity), 20f);
                }
                break;

            case 2: // ODDITY — no oddity prototype exists in-tree yet, so the list is empty and this no-ops.
                if (comp.OddityRewards.Count > 0)
                    SpawnAtPosition(_random.Pick(comp.OddityRewards), xform.Coordinates);
                break;

            case 3: // STAT_BUFF — Eris stat_buff_power (10) / duration (20 min).
            {
                var skill = _random.Pick(MiracleSkills);
                foreach (var (body, _) in FaithfulInRange(eye, comp))
                {
                    if (TryComp<MobSkillComponent>(body, out var mobSkill))
                        _skill.SetUniqueBuff((body, mobSkill), "EyeOfTheProtector", 10, skill, TimeSpan.FromMinutes(20));
                }
                break;
            }

            case 4: // MATERIAL_REWARD
                SpawnAtPosition(_random.Pick(MiracleMaterials), xform.Coordinates);
                break;

            case 5: // ENERGY_REWARD — restore the cruciform's holiness to full.
                foreach (var (_, cruciform) in FaithfulInRange(eye, comp))
                {
                    if (TryComp<CruciformComponent>(cruciform, out var state))
                    {
                        state.Holiness = state.MaxHoliness;
                        Dirty(cruciform, state);
                    }
                }
                break;
        }
    }

    /// <summary>Active faithful bodies (and their cruciform implants) within the observation radius.</summary>
    private List<(EntityUid Body, EntityUid Cruciform)> FaithfulInRange(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        var result = new List<(EntityUid, EntityUid)>();
        var xform = Transform(eye);
        var bearers = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (bearers.MoveNext(out var body, out var bearer, out var bodyXform))
        {
            if (bearer.Cruciform is not { } cruciform ||
                !TryComp<CruciformComponent>(cruciform, out var state) ||
                !state.Active ||
                bodyXform.MapID != xform.MapID)
                continue;

            if ((bodyXform.WorldPosition - xform.WorldPosition).Length() > comp.ObservationRadius)
                continue;

            result.Add((body, cruciform));
        }

        return result;
    }
}
