using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris operating computer (machinery/computer/Operating.dm): a console that
/// locates the closest operating table and continuously reports the vitals of the
/// patient buckled to it — status, critical health, damage bars, blood level, pulse.
/// </summary>
public sealed partial class OxydOperatingComputerSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;

    private float _refreshRemaining;

    public override void Initialize()
    {
        // Push on open AND keep re-pushing while open: the open-time push races client
        // window creation, and Eris refresh()es the page continuously anyway.
        SubscribeLocalEvent<OxydOperatingComputerComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
    }

    private void OnUiOpen(EntityUid uid, OxydOperatingComputerComponent comp, AfterActivatableUIOpenEvent args)
        => PushState(uid, comp);

    /// <summary>Nearest operating table (Eris locates one in the cardinal directions;
    /// a radius search also covers diagonal tables).</summary>
    private EntityUid? FindTable(EntityUid uid, OxydOperatingComputerComponent comp)
    {
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var pos = Transform(uid).Coordinates;

        foreach (var table in _lookup.GetEntitiesInRange<OxydOperatingTableComponent>(pos, comp.TableSearchRadius))
        {
            var dist = (Transform(table).Coordinates.Position - pos.Position).Length();
            if (dist < bestDist)
            {
                best = table;
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>The mob buckled to the table (Eris table.victim via check_victim).</summary>
    private EntityUid? FindPatient(EntityUid uid, OxydOperatingComputerComponent comp)
    {
        if (FindTable(uid, comp) is not { } table ||
            !TryComp<StrapComponent>(table, out var strap))
            return null;

        // FirstOrDefault returns EntityUid.Invalid (not null) on an empty strap -
        // is-{}-pattern treats it as a patient and Name() throws the server down.
        var patient = strap.BuckledEntities.FirstOrDefault(e => HasComp<MobStateComponent>(e));
        return patient.IsValid() ? patient : null;
    }

    private void PushState(EntityUid uid, OxydOperatingComputerComponent comp)
    {
        var state = new OxydOperatingComputerState();

        if (FindPatient(uid, comp) is { } patient)
        {
            state.HasPatient = true;
            state.PatientName = Name(patient);
            state.Alive = _mobs.IsAlive(patient);
            state.Critical = _mobs.IsCritical(patient);

            if (TryComp<DamageableComponent>(patient, out var dmg))
            {
                var spec = _damage.GetPositiveDamage((patient, dmg));
                var damage = spec.GetTotal().Float();
                var crit = _mobThreshold.GetThresholdForState(patient, MobState.Critical).Float();
                state.HealthPercent = crit > 0
                    ? Math.Clamp(100f - damage / crit * 100f, 0f, 100f)
                    : Math.Max(0f, 100f - damage);

                state.BruteLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Brute);
                state.BurnLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Burn);
                state.ToxinLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Toxin);
                state.OxyLoss = OxydDamageTypes.Sum(spec, OxydDamageTypes.Airloss);
            }

            // Eris "Organ Health": worst external organ as a percentage of healthy.
            var organHealth = 100f;
            foreach (var (_, organ, surg) in _wounds.GetOrgans(patient))
            {
                if (!OxydWoundSystem.IsExternal(organ))
                    continue;
                organHealth = Math.Min(organHealth, Math.Max(0f,
                    100f - surg.OrganDamage / OxydOrganSurgeryComponent.OrganMaxDamage * 100f));
            }
            state.OrganHealth = organHealth;

            if (TryComp<BloodstreamComponent>(patient, out var stream))
            {
                state.HasBlood = true;
                // GetBloodLevel: 0..MaxVolumeModifier, where 1.0 = full bloodstream.
                state.BloodPercent = _bloodstream.GetBloodLevel((patient, stream)) * 100f;
                if (_solutions.TryGetSolution(patient, stream.BloodSolutionName,
                        out _, out var blood))
                    state.BloodVolume = blood.Volume.Float();
            }

            state.Pulse = _wounds.ClassifyPulse(patient, state.Critical, !state.Alive);
        }

        _ui.SetUiState(uid, OxydOperatingComputerUiKey.Key, state);
    }

    public override void Update(float frameTime)
    {
        _refreshRemaining -= frameTime;
        if (_refreshRemaining > 0)
            return;
        _refreshRemaining = 1f;

        var query = EntityQueryEnumerator<OxydOperatingComputerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, OxydOperatingComputerUiKey.Key))
                PushState(uid, comp);
        }
    }
}
