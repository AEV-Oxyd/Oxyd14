using System.Collections.Frozen;
using System.Linq;
using Content.Server._Oxyd.Medical;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Body.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.LandMines;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
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
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly CruciformUpgradeSystem _upgrades = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly PlantGrowthSystem _plantGrowth = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly AddictionSystem _addiction = default!;
    [Dependency] private readonly PainSystem _pain = default!;
    [Dependency] private readonly RoboticOrganSystem _roboticOrgans = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>Hostile fauna, matching the obelisk's set (the fork's simple-hostile marker).</summary>
    private static readonly FrozenSet<ProtoId<NpcFactionPrototype>> HostileFauna =
        new ProtoId<NpcFactionPrototype>[] { "Dragon", "SimpleHostile", "Xeno" }.ToFrozenSet();

    /// <summary>
    /// Eris Rejection: detach robotic limbs and expel foreign implants. Preserve the cruciform and natural organs.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRejectForeignBody(Entity<MobStateComponent> ent, ref LitanyRejectForeignBodyEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var shed = _roboticOrgans.Reject(ent.Owner);
        if (TryComp<ImplantedComponent>(ent.Owner, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities.ToArray())
            {
                if (!HasComp<CruciformComponent>(implant) &&
                    _containers.Remove(implant, implanted.ImplantContainer, force: true,
                        destination: Transform(ent.Owner).Coordinates))
                    shed++;
            }
        }

        if (shed > 0)
        {
            _damageable.TryChangeDamage(ent.Owner,
                new DamageSpecifier { DamageDict = { ["Blunt"] = 20f * shed } }, origin: ent.Owner);
            _popup.PopupEntity(Loc.GetString("oxyd-litany-rejection-shed"), ent.Owner, ent.Owner, PopupType.LargeCaution);
        }
    }

    /// <summary>
    /// Eris <c>rituals/base.dm:92-120</c>: scan hostile fauna within 14 m and traps within 7 m.
    /// The fork's trap marker is <see cref="LandMineComponent"/>. Eris also hides a 20 percent
    /// false-negative chance; kept for fidelity.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRevealAdversaries(Entity<MobStateComponent> ent, ref LitanyRevealAdversariesEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (_random.Prob(0.2f))
        {
            _popup.PopupEntity(Loc.GetString("oxyd-litany-reveal-none"), ent.Owner, ent.Owner);
            return;
        }

        var xform = Transform(ent.Owner);
        var found = false;
        foreach (var (mob, faction) in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(xform.Coordinates, 14f))
        {
            if (HasComp<HumanoidProfileComponent>(mob) || HasComp<CruciformBearerComponent>(mob))
                continue;
            if (!_factions.IsMemberOfAny((mob, faction), HostileFauna))
                continue;

            found = true;
            break;
        }

        if (!found)
            found = _lookup.GetEntitiesInRange<LandMineComponent>(xform.Coordinates, 7f).Count > 0;

        _popup.PopupEntity(
            Loc.GetString(found ? "oxyd-litany-reveal-hostiles" : "oxyd-litany-reveal-none"),
            ent.Owner,
            ent.Owner);
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
        _pain.SuppressPain(ent.Owner, "WordsOfPurging", 15, 2);

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

        args.Handled = true;

        if (!_cruciform.TryGetCruciform(ent.Owner, out var cruciform, out var comp))
            return;

        var removed = 0;
        while (removed < 16 && _upgrades.TryUninstallUpgrade(cruciform, comp))
            removed++;

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
            _plantGrowth.AdjustGrowthBoost((plant, growth), args.Multiplier, args.Duration);
            boosted++;
        }

        if (boosted == 0)
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("oxyd-litany-growth"), ent.Owner, ent.Owner);
    }
}
