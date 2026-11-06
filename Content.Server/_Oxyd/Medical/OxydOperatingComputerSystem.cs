using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris operating computer (machinery/computer/Operating.dm): a console that
/// reports the vitals of the patient buckled to a device-linked operating table —
/// status, critical health, damage bars, blood level, pulse. Links are created
/// automatically when a table is placed near a computer, when a computer is placed
/// near tables, and when a patient is strapped to a table (DeviceLinkSystem).
/// </summary>
public sealed partial class OxydOperatingComputerSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private OxydWoundSystem _wounds = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;

    /// <summary>Eris computer->table device-link port pair (range lives on
    /// DeviceLinkSourceComponent in machines.yml).</summary>
    private static readonly ProtoId<SourcePortPrototype> OpComputerPort = "OxydOpComputer";
    private static readonly ProtoId<SinkPortPrototype> OpTablePort = "OxydOpTable";

    private float _refreshRemaining;

    [SubscribeLocalEvent]
    private void OnUiOpen(Entity<OxydOperatingComputerComponent> ent, ref BoundUIOpenedEvent args)
    {
        EnsureComp<OxydActiveInterfaceComponent>(ent);
        PushState(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUiClose(Entity<OxydOperatingComputerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!_ui.IsUiOpen(ent.Owner, OxydOperatingComputerUiKey.Key))
            RemComp<OxydActiveInterfaceComponent>(ent.Owner);
    }

    // ---- auto-linking (table placed / patient strapped / computer placed) ----
    [SubscribeLocalEvent]
    private void OnTableMapInit(Entity<OxydOperatingTableComponent> ent, ref MapInitEvent args)
        => AutoLinkTable(ent.Owner);

    [SubscribeLocalEvent]
    private void OnTableStrapped(Entity<OxydOperatingTableComponent> ent, ref StrappedEvent args)
        => AutoLinkTable(ent.Owner);

    [SubscribeLocalEvent]
    private void OnComputerMapInit(Entity<OxydOperatingComputerComponent> ent, ref MapInitEvent args)
        => AutoLinkComputer(ent.Owner);

    /// <summary>Link every operating computer in range of this table.</summary>
    private void AutoLinkTable(EntityUid table)
    {
        if (!TryComp<DeviceLinkSinkComponent>(table, out var tableSink))
            return;
        var tableXform = Transform(table);
        var query = EntityQueryEnumerator<DeviceLinkSourceComponent, OxydOperatingComputerComponent, TransformComponent>();
        while (query.MoveNext(out var compUid, out var source, out _, out var cxform))
        {
            if (TerminatingOrDeleted(compUid))
                continue;
            if (!tableXform.Coordinates.TryDistance(EntityManager, cxform.Coordinates, out var dist)
                || dist > source.Range)
                continue;
            _deviceLink.SaveLinks(null, (compUid, source), (table, tableSink),
                [new DeviceLink(OpComputerPort, OpTablePort)]);
        }
    }

    /// <summary>Link every operating table in range of this computer.</summary>
    private void AutoLinkComputer(EntityUid computer)
    {
        if (!TryComp<DeviceLinkSourceComponent>(computer, out var source))
            return;
        var cxform = Transform(computer);
        var query = EntityQueryEnumerator<DeviceLinkSinkComponent, OxydOperatingTableComponent, TransformComponent>();
        while (query.MoveNext(out var table, out var sink, out _, out var txform))
        {
            if (TerminatingOrDeleted(table))
                continue;
            if (!cxform.Coordinates.TryDistance(EntityManager, txform.Coordinates, out var dist)
                || dist > source.Range)
                continue;
            _deviceLink.SaveLinks(null, (computer, source), (table, sink),
                [new DeviceLink(OpComputerPort, OpTablePort)]);
        }
    }

    /// <summary>The mob buckled to a device-linked operating table (Eris table.victim
    /// via check_victim). Links that drifted out of range are pruned so a moved
    /// table stops reporting patients.</summary>
    private EntityUid? FindPatient(EntityUid uid)
    {
        if (!TryComp<DeviceLinkSourceComponent>(uid, out var source))
            return null;

        foreach (var table in _deviceLink.GetLinkedSinks((uid, source), OpComputerPort))
        {
            var inRange = Transform(uid).Coordinates.TryDistance(EntityManager,
                Transform(table).Coordinates, out var dist) && dist <= source.Range;
            if (!inRange)
            {
                if (TryComp<DeviceLinkSinkComponent>(table, out var tableSink))
                    _deviceLink.RemoveSinkFromSource((uid, source), (table, tableSink));
                continue;
            }
            if (!TryComp<StrapComponent>(table, out var strap))
                continue;
            // FirstOrDefault returns EntityUid.Invalid (not null) on an empty strap -
            // is-{}-pattern treats it as a patient and Name() throws the server down.
            var patient = strap.BuckledEntities.FirstOrDefault(e => HasComp<MobStateComponent>(e));
            if (patient.IsValid())
                return patient;
        }
        return null;
    }

    private void PushState(EntityUid uid)
    {
        var state = new OxydOperatingComputerState();

        if (FindPatient(uid) is { } patient)
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
                if (!_wounds.IsExternal(organ))
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

        var query = EntityQueryEnumerator<OxydOperatingComputerComponent, OxydActiveInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out _))
            PushState(uid);
    }
}
