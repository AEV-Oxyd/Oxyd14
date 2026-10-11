using System.Linq;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared.Humanoid;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Utility;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared._Oxyd.Skills;
using Content.Shared.StatusEffectNew;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// P3.2: the Eye of the Protector banks observation from active faithful in its radius.
/// <see cref="Update"/> only paces the scan; the work lives in <see cref="Scan"/> so tests drive it
/// without waiting out <see cref="EyeOfTheProtectorComponent.ScanInterval"/>.
/// </summary>
public sealed partial class EyeOfTheProtectorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NeoTheologyMachineSystem _machines = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SanitySystem _sanity = default!;
    [Dependency] private SharedSkillSystem _skill = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private LitanyEffectSystem _effects = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnActivityChanged(ref CruciformActivityChangedEvent args)
    {
        if (args.Body is { } body && FindEye(body) is { } eye)
            AddObservation(eye, args.Active ? 50 : -50);
    }

    public bool ApplyOffering(EntityUid eye, float power, IReadOnlyList<NeoTheologyMiracle> rewards)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp) || !_machines.IsOperational(eye))
            return false;
        comp.Power += power;
        comp.NextRewards = rewards.ToList();
        Dirty(eye, comp);
        return true;
    }

    /// <summary>P3.7: push a read-only status snapshot when the Eye's UI is opened.</summary>
    [SubscribeLocalEvent]
    private void OnUiOpened(Entity<EyeOfTheProtectorComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        PushEyeState(ent);
    }

    private void PushEyeState(Entity<EyeOfTheProtectorComponent> ent)
    {
        var cooldown = ent.Comp.NextMiracle - _timing.CurTime;
        if (cooldown < TimeSpan.Zero)
            cooldown = TimeSpan.Zero;

        _ui.SetUiState(ent.Owner, EyeOfTheProtectorUiKey.Key, new EyeOfTheProtectorState(
            ent.Comp.Observation,
            ent.Comp.ArmamentsPoints,
            ent.Comp.MaxArmamentsPoints,
            cooldown,
            BuildArmamentEntries(ent.Comp)));
    }

    /// <summary>
    /// Eris <c>nano_ui_data</c>: every concrete armament priced for the Eye's own armory list.
    /// </summary>
    private List<ArmamentEntry> BuildArmamentEntries(EyeOfTheProtectorComponent comp)
    {
        var entries = new List<ArmamentEntry>();
        foreach (var armament in ProtoMan.EnumeratePrototypes<ArmamentPrototype>())
        {
            var cost = GetCost(comp, armament);
            entries.Add(new ArmamentEntry(
                armament.ID,
                Loc.GetString(armament.Name),
                armament.Desc is { } desc ? Loc.GetString(desc) : string.Empty,
                cost,
                comp.ArmamentsPoints >= cost));
        }
        return entries;
    }

    /// <summary>Discounted price an armament sells for against this Eye's purchase history.</summary>
    public int GetCost(EyeOfTheProtectorComponent component, ArmamentPrototype armament)
    {
        return armament.GetCost(GetDiscount(component, armament));
    }

    private int GetPurchaseCount(EyeOfTheProtectorComponent component, string armamentId)
    {
        return component.PurchaseCount.GetValueOrDefault(armamentId);
    }

    private int GetDiscount(EyeOfTheProtectorComponent component, ArmamentPrototype armament)
    {
        return armament.GetDiscount(GetPurchaseCount(component, armament.ID));
    }

    /// <summary>
    /// Eris <c>datum/armament/purchase</c> minus the buyer gates: debit the Eye's bank, count the
    /// purchase (raising the point ceiling on each product's first sale) and hand the armament
    /// back so the caller can drop it on its own turf. Returns null when the bank or the id refuses.
    /// </summary>
    public bool TryBuyArmament(EntityUid eye, string armamentId, out ArmamentPrototype? armament)
    {
        armament = null;
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp) ||
            !ProtoMan.TryIndex(armamentId, out armament))
            return false;

        if (!TrySpendArmaments(eye, GetCost(comp, armament)))
            return false;

        comp.PurchaseCount[armamentId] = GetPurchaseCount(comp, armamentId) + 1;
        if (GetPurchaseCount(comp, armamentId) == 1)
            comp.MaxArmamentsPoints += armament.MaxPointsIncrease;
        comp.FirstPurchaseMade = true;
        Dirty(eye, comp);
        return true;
    }

    /// <summary>
    /// Eris <c>eopt.tmpl</c> purchase: the Eye's own armory. The buyer must be an in-range disciple;
    /// the disk appears on the Eye's turf like every Eris armament purchase (<c>new path(get_turf(eotp))</c>).
    /// </summary>
    public bool TryPurchaseFromEye(EntityUid eye, EntityUid user, string armamentId)
    {
        if (!_machines.IsOperational(eye) || !TryComp<EyeOfTheProtectorComponent>(eye, out var comp))
            return false;

        // Eris is_neotheology_disciple: an active cruciform carrying a configured profile.
        if (!_cruciform.TryGetCruciform(user, out _, out var cruciform))
            return false;

        if (_cruciform.GetRules() is not { } rules || !rules.Profiles.Contains(cruciform.Profile))
            return false;

        var eyeXform = Transform(eye);
        if (eyeXform.MapID != Transform(user).MapID)
            return false;

        if ((_transform.GetWorldPosition(eyeXform) - _transform.GetWorldPosition(user)).Length() > comp.PurchaseRange)
            return false;

        if (!TryBuyArmament(eye, armamentId, out var armament) || armament is null)
            return false;

        SpawnAtPosition(armament.Path, eyeXform.Coordinates);
        PushEyeState((eye, comp));
        return true;
    }

    [SubscribeLocalEvent]
    private void OnPurchaseMessage(EntityUid uid, EyeOfTheProtectorComponent component, PurchaseArmamentMessage args)
    {
        TryPurchaseFromEye(uid, args.Actor, args.ArmamentId);
    }

    private const float EyeUiRefresh = 1f;
    private float _eyeUiAccum;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Re-push the read-only snapshot once a second while any Eye window is open — the
        // open-time push can race the client's window creation and leave labels empty.
        _eyeUiAccum += frameTime;
        if (_eyeUiAccum >= EyeUiRefresh)
        {
            _eyeUiAccum = 0f;
            var uiQuery = EntityQueryEnumerator<EyeOfTheProtectorComponent, UserInterfaceComponent>();
            while (uiQuery.MoveNext(out var uid, out var comp, out var ui))
            {
                if (_ui.IsUiOpen((uid, ui), EyeOfTheProtectorUiKey.Key))
                    PushEyeState((uid, comp));
            }
        }

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

        // A zero radius means nobody is in range. EntityLookup asserts a positive range.
        if (radius <= 0)
            return;

        var xform = Transform(source);
        foreach (var (body, _) in _lookup.GetEntitiesInRange<HumanoidProfileComponent>(xform.Coordinates, radius))
        {
            ObserveEntity(eye, body, comp);
        }
    }

    /// <summary>Records one visible human. Obelisks supply targets from their view tick.</summary>
    public void ObserveEntity(EntityUid eye, EntityUid body, EyeOfTheProtectorComponent? comp = null)
    {
        if (!Resolve(eye, ref comp) || !_machines.IsOperational(eye) ||
            !HasComp<HumanoidProfileComponent>(body) || comp.Scanned.ContainsKey(body))
            return;

        var faithful = TryComp<CruciformBearerComponent>(body, out var bearer) &&
            bearer.Cruciform is { } implant && TryComp<CruciformComponent>(implant, out var state) && state.Active;
        var before = comp.Observation;
        AddObservation(eye, faithful ? comp.ObservationPerFaithful : comp.ObservationPerNeutral);
        if (comp.Scanned.Count == 0)
            comp.NextRescan = _timing.CurTime + comp.RescanInterval;
        comp.Scanned.Add(body, comp.Observation - before);
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
        if (comp.ObservationRadius > 0)
        {
            foreach (var (body, bearer) in _lookup.GetEntitiesInRange<CruciformBearerComponent>(xform.Coordinates, comp.ObservationRadius))
            {
                if (bearer.Cruciform is not { } cruciform ||
                    !TryComp<CruciformComponent>(cruciform, out var state) ||
                    !state.Active)
                    continue;

                _statusEffects.TryAddStatusEffectDuration(body, NeoTheologyPrototypes.EyeBlessingStatusEnt, comp.FaithfulBlessingDuration);
            }
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

    /// <summary>
    /// Eris <c>updatePower()</c>: bank power from observation plus connected faithful
    /// globally, and release a miracle whenever the bank fills.
    /// </summary>
    public void UpdatePower(EntityUid eye, EyeOfTheProtectorComponent comp)
    {
        if (!_machines.IsOperational(eye) || _timing.CurTime < comp.NextPowerUpdate)
            return;

        comp.NextPowerUpdate = _timing.CurTime + comp.PowerInterval;
        comp.NextMiracle = comp.NextPowerUpdate;

        var gain = comp.PowerGainBase +
            Math.Clamp(comp.Observation, comp.MinObservation, comp.MaxObservation) / 100f;
        // Source power counts connected disciples globally, not only bodies near the Eye.
        gain += EnumerateFaithful().Count(f => HasComp<ActorComponent>(f.Body));
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

        var rewards = comp.NextRewards.Count > 0
            ? comp.NextRewards.ToList()
            : Enum.GetValues<NeoTheologyMiracle>().ToList();
        if (comp.OddityReleased || comp.OddityRewards.Count == 0)
            rewards.Remove(NeoTheologyMiracle.Oddity);
        comp.NextRewards.Clear();
        if (rewards.Count == 0)
            return;
        FireMiracle(eye, _random.Pick(rewards), comp);
    }

    public void FireMiracle(EntityUid eye, NeoTheologyMiracle reward, EyeOfTheProtectorComponent? comp = null)
    {
        if (!Resolve(eye, ref comp) || !_machines.IsOperational(eye))
            return;
        var xform = Transform(eye);
        switch (reward)
        {
            case NeoTheologyMiracle.Alert:
            {
                var faithful = EnumerateFaithful();
                if (faithful.Count == 0)
                    break;
                EntityUid? threat = null;
                var threats = EntityQueryEnumerator<NeoTheologyThreatComponent>();
                while (threats.MoveNext(out var target, out _))
                    if (_mobState.IsAlive(target) && !TerminatingOrDeleted(target) &&
                        !EntityManager.IsQueuedForDeletion(target))
                    {
                        threat = target;
                        break;
                    }
                if (threat is { } enemy)
                {
                    var preacher = faithful.FirstOrDefault(f =>
                        Comp<CruciformComponent>(f.Cruciform).InstalledModules.Contains(NeoTheologyPrototypes.PriestModule));
                    var recipient = preacher.Body == default ? _random.Pick(faithful).Body : preacher.Body;
                    _effects.DeliverSocialNotice(recipient, Loc.GetString("oxyd-eotp-threat",
                        ("location", FormattedMessage.EscapeText(_effects.DescribeLocation(enemy)))));
                }
                else
                {
                    foreach (var (body, _) in faithful)
                    {
                        _effects.DeliverSocialNotice(body, Loc.GetString("oxyd-eotp-calm"));
                        if (_random.Prob(0.5f) && TryComp<SanityComponent>(body, out var sanity))
                            _sanity.ApplySanityDelta((body, sanity), SanitySource.Belief, 20);
                    }
                }
                break;
            }

            case NeoTheologyMiracle.Inspiration: // Native insight replaces Eris's perk breakdown.
                foreach (var (body, _) in EnumerateFaithful())
                {
                    if (_random.Prob(0.5f) && TryComp<SanityComponent>(body, out var sanity))
                        _sanity.GiveInsight((body, sanity), 20f);
                }
                break;

            case NeoTheologyMiracle.Oddity:
                if (!comp.OddityReleased && comp.OddityRewards.Count > 0)
                {
                    SpawnAtPosition(_random.Pick(comp.OddityRewards), xform.Coordinates);
                    comp.OddityReleased = true;
                }
                break;

            case NeoTheologyMiracle.StatBuff:
            {
                var skill = _random.Pick(comp.MiracleSkills);
                foreach (var (body, _) in EnumerateFaithful())
                {
                    if (TryComp<MobSkillComponent>(body, out var mobSkill))
                        _skill.SetUniqueBuff((body, mobSkill), comp.MiracleBuffId, 10, skill, TimeSpan.FromMinutes(20));
                }
                break;
            }

            case NeoTheologyMiracle.Material:
                SpawnAtPosition(_random.Pick(comp.MiracleMaterials), xform.Coordinates);
                break;

            case NeoTheologyMiracle.Energy:
                foreach (var (_, cruciform) in EnumerateFaithful())
                {
                    if (TryComp<CruciformComponent>(cruciform, out var state))
                    {
                        state.EnergyMiracles++;
                        _cruciform.RecomputeRegeneration(state);
                        Dirty(cruciform, state);
                    }
                }
                break;
        }
    }

    /// <summary>The source disciple registry, derived from live linked implants rather than stale registrations.</summary>
    public List<(EntityUid Body, EntityUid Cruciform)> EnumerateFaithful()
    {
        var result = new List<(EntityUid, EntityUid)>();
        var query = EntityQueryEnumerator<CruciformBearerComponent>();
        while (query.MoveNext(out var body, out _))
            if (!TerminatingOrDeleted(body) && !EntityManager.IsQueuedForDeletion(body) &&
                _cruciform.TryGetCruciform(body, out var implant, out _))
                result.Add((body, implant));
        return result;
    }

}
