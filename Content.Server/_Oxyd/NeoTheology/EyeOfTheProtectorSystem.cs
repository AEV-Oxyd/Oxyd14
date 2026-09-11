using System.Linq;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared.Humanoid;
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
    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out var uid, out var eye))
        {
            if (!_machines.IsOperational(uid))
                continue;

            UpdatePower(uid, eye);
            ForgetOneObservation(uid, eye);

            if (now < eye.NextScan)
                continue;

            eye.NextScan = now + eye.ScanInterval;
            Scan(uid, eye);
        }
    }

    public void AddObservation(EntityUid eye, float amount)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp))
            return;

        comp.Observation = Math.Clamp(comp.Observation + amount, comp.MinObservation, comp.MaxObservation);
        Dirty(eye, comp);
    }

    /// <summary>Reverse one recorded award each ten minutes, as Eris does.</summary>
    private void ForgetOneObservation(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        if (_timing.CurTime < comp.NextRescan || comp.Scanned.Count == 0)
            return;

        var body = _random.Pick(comp.Scanned.Keys.ToList());
        AddObservation(eye, -comp.Scanned[body]);
        comp.Scanned.Remove(body);
        comp.NextRescan = _timing.CurTime + comp.RescanInterval;
    }

    /// <summary>Eye and obelisks share this observation record.</summary>
    public void ObserveArea(EntityUid eye, EntityUid source, float radius)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp) || !_machines.IsOperational(eye))
            return;

        var xform = Transform(source);
        var humans = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent>();
        while (humans.MoveNext(out var body, out _, out var bodyXform))
        {
            if (bodyXform.MapID != xform.MapID ||
                (bodyXform.WorldPosition - xform.WorldPosition).Length() > radius || comp.Scanned.ContainsKey(body))
                continue;

            var faithful = TryComp<CruciformBearerComponent>(body, out var bearer) &&
                bearer.Cruciform is { } implant && TryComp<CruciformComponent>(implant, out var state) && state.Active;
            var before = comp.Observation;
            AddObservation(eye, faithful ? comp.ObservationPerFaithful : comp.ObservationPerNeutral);
            if (comp.Scanned.Count == 0)
                comp.NextRescan = _timing.CurTime + comp.RescanInterval;
            comp.Scanned.Add(body, comp.Observation - before);
        }
    }

    /// <summary>Refresh blessings without awarding an observed body again.</summary>
    /// <remarks>
    /// ponytail: Eris also penalises mutants (<c>mutation_index</c>) and carrion (<c>is_carrion</c>)
    /// here via ObservationPerFaithless. Neither marker exists in this fork (only Botany plant
    /// mutations), so the penalty is deferred until a real marker lands — do not map it onto a
    /// guessed stand-in. ObservationPerFaithless stays unused until then.
    /// </remarks>
    public void Scan(EntityUid eye, EyeOfTheProtectorComponent? comp = null)
    {
        if (!Resolve(eye, ref comp) || !_machines.IsOperational(eye))
            return;

        ForgetOneObservation(eye, comp);
        ObserveArea(eye, eye, comp.ObservationRadius);
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

            _statusEffects.TryAddStatusEffectDuration(body, "OxydNtEyeBlessing", comp.FaithfulBlessingDuration);
        }

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
            if (xform.MapID == map && _machines.IsOperational(uid))
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
    /// Eris <c>updatePower()</c>: bank power from the observation level plus one per faithful
    /// in range, and release a miracle whenever the bank fills.
    /// </summary>
    public void UpdatePower(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        if (!_machines.IsOperational(eye) || _timing.CurTime < comp.NextPowerUpdate)
            return;

        comp.NextPowerUpdate = _timing.CurTime + comp.PowerInterval;
        comp.NextMiracle = comp.NextPowerUpdate;

        var gain = comp.PowerGainBase +
            Math.Clamp(comp.Observation, comp.MinObservation, comp.MaxObservation) / 100f;
        gain += FaithfulInRange(eye, comp).Count;
        comp.Power += gain;

        while (comp.Power >= comp.MaxPower)
        {
            comp.Power -= comp.MaxPower;
            ReleaseMiracle(eye, comp);
        }

        Dirty(eye, comp);
    }

    /// <summary>Eris <c>power_release()</c>: bank armament points, then fire one random reward.</summary>
    private void ReleaseMiracle(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        comp.ArmamentsPoints = Math.Min(comp.ArmamentsPoints + comp.ArmamentsRate, comp.MaxArmamentsPoints);

        // Eris GLOB.miracle_points++ at the end of power_release().
        comp.MiraclePoints++;
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
