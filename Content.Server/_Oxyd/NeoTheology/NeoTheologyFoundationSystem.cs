using System.Collections.Frozen;
using System.Linq;
using Content.Server._Oxyd.Medical;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Body.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Verbs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.LandMines;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Examine;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Server handlers for the Phase 4 foundation bridges whose capability is not a machine:
/// Rejection, Reveal Adversaries, Words of Purging, Atonement/Penance, Asacris and
/// Accelerated Growth.
/// </summary>
public sealed partial class NeoTheologyFoundationSystem : EntitySystem
{
    private const float ShedBluntPerStack = 20f;
    private const int AtheistHeatMin = 5, AtheistHeatMax = 25;
    private const int EjectCellularMin = 55, EjectCellularMax = 60;
    private const int EjectAsphyxiationMin = 100, EjectAsphyxiationMax = 150;
    private const int EjectHeatMin = 100, EjectHeatMax = 175;
    private const int EjectRadiationMin = 40, EjectRadiationMax = 60;

    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private LitanyEffectSystem _effects = default!;
    [Dependency] private CruciformUpgradeSystem _upgrades = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private PlantGrowthSystem _plantGrowth = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AddictionSystem _addiction = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private RoboticOrganSystem _roboticOrgans = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private IRobustRandom _random = default!;


    /// <summary>
    /// Eris Rejection: detach robotic limbs and expel foreign implants. Preserve the cruciform and natural organs.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRejectForeignBody(Entity<MobStateComponent> ent, ref LitanyRejectForeignBodyEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        Purify(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnRejectedInsert(Entity<RejectedImplantComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Container.ID != ImplanterComponent.ImplantSlotId)
            return;
        args.Cancel();
        if (TryComp<SubdermalImplantComponent>(ent.Owner, out var implant))
        {
            implant.ImplantedEntity = null;
            Dirty(ent.Owner, implant);
        }
    }

    /// <summary>The same purity path for explicit Rejection and the active implant's periodic cleanse.</summary>
    public int Purify(EntityUid body, bool cleanseMutation = false)
    {
        if (HasComp<GodbloodMutationComponent>(body))
            return 0;
        var shed = _roboticOrgans.Reject(body);
        if (TryComp<ImplantedComponent>(body, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities.ToArray())
            {
                if (HasComp<CruciformComponent>(implant) || HasComp<CruciformResistantComponent>(implant))
                    continue;
                if (_containers.Remove(implant, implanted.ImplantContainer, force: true,
                        destination: Transform(body).Coordinates))
                {
                    EnsureComp<RejectedImplantComponent>(implant);
                    shed++;
                }
            }
        }

        if (TryComp<EmbeddedContainerComponent>(body, out var embedded))
        {
            foreach (var item in embedded.EmbeddedObjects.ToArray())
                if (TryComp<EmbeddableProjectileComponent>(item, out var projectile) && projectile.EmbeddedIntoUid == body)
                {
                    _projectiles.EmbedDetach(item, projectile);
                    shed++;
                }
        }
        if (shed > 0)
        {
            _damageable.TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { [NeoTheologyPrototypes.DamageBlunt] = ShedBluntPerStack * shed } }, origin: body);
            _popup.PopupEntity(Loc.GetString("oxyd-litany-rejection-shed"), body, body, PopupType.LargeCaution);
        }
        if (cleanseMutation && HasComp<AtheistMutationComponent>(body))
        {
            RemComp<AtheistMutationComponent>(body);
            _damageable.TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { [NeoTheologyPrototypes.DamageHeat] = _random.Next(AtheistHeatMin, AtheistHeatMax + 1) } });
        }
        return shed;
    }

    [SubscribeLocalEvent]
    private void OnHardEjectVerb(Entity<CruciformBearerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User != ent.Owner ||
            !_cruciform.TryGetCruciformEntity(ent.Owner, out _, out _))
            return;
        var body = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("oxyd-nt-hard-eject"),
            Act = () => TryHardEject(body),
        });
    }

    public bool TryHardEject(EntityUid body)
    {
        if (!_cruciform.TryGetCruciformEntity(body, out var implant, out _) ||
            !TryComp<ImplantedComponent>(body, out var installed) ||
            !_containers.Remove(implant, installed.ImplantContainer, force: true,
                destination: Transform(body).Coordinates))
            return false;
        if (!_mobState.IsDead(body))
            _damageable.TryChangeDamage(body, new DamageSpecifier
            {
                DamageDict =
                {
                    [NeoTheologyPrototypes.DamageCellular] = _random.Next(EjectCellularMin, EjectCellularMax + 1),
                    [NeoTheologyPrototypes.DamageAsphyxiation] = _random.Next(EjectAsphyxiationMin, EjectAsphyxiationMax + 1),
                    [NeoTheologyPrototypes.DamageHeat] = _random.Next(EjectHeatMin, EjectHeatMax + 1),
                    [NeoTheologyPrototypes.DamageRadiation] = _random.Next(EjectRadiationMin, EjectRadiationMax + 1),
                },
            }, ignoreResistances: true);
        return true;
    }

    /// <summary>
    /// Eris <c>rituals/base.dm:92-120</c>: scan hostile fauna within 14 m and traps within 7 m.
    /// The fork's trap marker is <see cref="LandMineComponent"/>. Eris has a hidden 20 percent
    /// failure and a separate 80 percent trap roll.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRevealAdversaries(Entity<MobStateComponent> ent, ref LitanyRevealAdversariesEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_random.Prob(0.2f))
        {
            _effects.DeliverSocialNotice(ent.Owner, Loc.GetString("oxyd-litany-reveal-none"));
            return;
        }

        var xform = Transform(ent.Owner);
        var found = false;
        foreach (var (mob, faction) in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(xform.Coordinates, 14f))
        {
            if (_mobState.IsDead(mob) || HasComp<HumanoidProfileComponent>(mob) || HasComp<CruciformBearerComponent>(mob))
                continue;
            if (_cruciform.GetRules() is not { } rules ||
                !_factions.IsMemberOfAny((mob, faction), rules.HostileFactions))
                continue;

            found = true;
            break;
        }

        if (found)
            _effects.DeliverSocialNotice(ent.Owner, Loc.GetString("oxyd-litany-reveal-hostiles"));

        var trap = _random.Prob(0.8f) &&
            _lookup.GetEntitiesInRange<LandMineComponent>(xform.Coordinates, 7f)
                .Any(mine => _examine.InRangeUnOccluded(ent.Owner, mine.Owner, 7f, predicate: null));
        if (trap)
            _effects.DeliverSocialNotice(ent.Owner, Loc.GetString("oxyd-litany-reveal-traps"));
        else if (!found)
            _effects.DeliverSocialNotice(ent.Owner, Loc.GetString("oxyd-litany-reveal-none"));
    }

    /// <summary>
    /// Advances addiction recovery without deleting blood reagents.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPurgeAddiction(Entity<MobStateComponent> ent, ref LitanyPurgeAddictionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        _addiction.AdvanceRecovery(ent.Owner, 15);
        _pain.SuppressPain(ent.Owner, "OxydAnalgesicWordsOfPurging", 15, 2);

        _popup.PopupEntity(Loc.GetString("oxyd-litany-purging"), ent.Owner, ent.Owner);
    }

    /// <summary>
    /// Eris <c>rituals/priest.dm:173-211</c> and <c>rituals/inquisitor.dm:33-65</c>:
    /// <c>adjustHalLoss(50)</c>. Adds temporary pain without wound or stamina damage.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPain(Entity<MobStateComponent> ent, ref LitanyPainEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _pain.AddPain(ent.Owner, args.Amount);
        _popup.PopupEntity(Loc.GetString("oxyd-litany-pain"), ent.Owner, ent.Owner, PopupType.LargeCaution);
    }

    /// <summary>Eris <c>rituals/priest.dm:68-90</c> (Asacris): strip installed upgrade modules, not rank modules.</summary>
    [SubscribeLocalEvent]
    private void OnRemoveUpgrades(Entity<MobStateComponent> ent, ref LitanyRemoveUpgradesEvent args)
    {
        if (args.Handled)
            return;

        if (!_cruciform.TryGetCruciform(ent.Owner, out var cruciform, out var comp))
            return;

        args.Handled = _upgrades.TryRemoveCoreUpgrades(cruciform, comp);
        if (!args.Handled)
            return;

        _popup.PopupEntity(Loc.GetString("oxyd-litany-asacris"), ent.Owner, ent.Owner);
    }

    /// <summary>
    /// Eris <c>rituals/agrolyte.dm:10-45</c>: every plant in view is boosted for five minutes.
    /// Fails when no plant is around, which lets the atomic commit refund the cast.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAcceleratedGrowth(Entity<MobStateComponent> ent, ref LitanyAcceleratedGrowthEvent args)
    {
        if (args.Handled)
            return;

        var xform = Transform(ent.Owner);
        var boosted = 0;
        foreach (var (plant, growth) in _lookup.GetEntitiesInRange<Content.Shared.Botany.Components.PlantGrowthComponent>(xform.Coordinates, 7f))
        {
            if (!_examine.InRangeUnOccluded(ent.Owner, plant, 7f, predicate: null))
                continue;
            if (!args.ValidateOnly)
                _plantGrowth.AdjustGrowthBoost((plant, growth), args.Multiplier, args.Duration);
            boosted++;
        }

        if (boosted == 0)
            return;

        args.Handled = true;
        if (!args.ValidateOnly)
            _popup.PopupEntity(Loc.GetString("oxyd-litany-growth"), ent.Owner, ent.Owner);
    }
}
