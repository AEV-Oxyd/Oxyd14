using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Runs the installed-upgrade behaviours Eris implements outside the item's stat deltas:
/// <c>natures_blessing</c> and <c>cleansing_presence</c> (auras), <c>martyr_gift</c> (death
/// burst), <c>speed_of_the_chosen</c> (movement) and <c>wrath_of_god</c> (melee damage).
/// The item is the source of truth: the system reads the cruciform's installed upgrade and
/// never keeps its own install list.
/// </summary>
public sealed partial class CruciformUpgradeBehaviorSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlantTraySystem _plantTray = default!;
    [Dependency] private readonly ViewCalcSystem _viewCalc = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private static readonly ProtoId<DamageGroupPrototype> BruteGroup = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> BurnGroup = "Burn";

    /// <summary>
    /// The aura fires on the view cadence, the same one-second heartbeat the bearer already
    /// gets for sight. No private timer and no full bearer scan.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCadence(Entity<CruciformBearerComponent> ent, ref ViewCadenceEvent args)
    {
        if (!_cruciform.TryGetCruciformEntity(ent.Owner, out _, out var component) || !component.Active)
            return;

        if (component.Upgrade is not { } upgrade ||
            !TryComp<CruciformUpgradeAuraComponent>(upgrade, out var aura))
            return;

        ApplyAura(ent.Owner, aura);
    }

    /// <summary>
    /// Eris fires the martyr burst from the death hook. The armed marker component turns that
    /// hook into a component subscription, so no per-tick death scan is needed.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMartyrDeath(Entity<CruciformMartyrArmedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (!_cruciform.TryGetCruciformEntity(ent.Owner, out var cruciform, out var component) ||
            component.Upgrade is not { } upgrade ||
            !TryComp<CruciformUpgradeMartyrComponent>(upgrade, out var martyr))
            return;

        TriggerMartyr(ent.Owner, cruciform, component, upgrade, martyr);
    }

    private void ApplyAura(EntityUid body, CruciformUpgradeAuraComponent aura)
    {
        if (aura.Radius <= 0)
            return;

        var coordinates = Transform(body).Coordinates;
        var origin = _xform.GetMapCoordinates(body);

        // Eris heals only wounded faithful in view and only above the threshold.
        foreach (var (target, damageable) in _lookup.GetEntitiesInRange<DamageableComponent>(coordinates, aura.Radius))
        {
            if (target == body || _mobState.IsDead(target) || !HasComp<CruciformBearerComponent>(target))
                continue;
            if (!_viewCalc.InLineOfSight(origin, target))
                continue;

            var groups = _damageable.GetDamagePerGroup((target, damageable));
            var heal = new DamageSpecifier();

            if (groups.GetValueOrDefault(BruteGroup) > aura.HealThreshold)
                AddGroupHeal(heal, damageable, BruteGroup, aura.BruteHealPerSecond);
            if (groups.GetValueOrDefault(BurnGroup) > aura.HealThreshold)
                AddGroupHeal(heal, damageable, BurnGroup, aura.BurnHealPerSecond);

            if (heal.DamageDict.Count > 0)
                _damageable.TryChangeDamage(target, heal);
        }

        if (aura.PlantHealPerSecond > 0)
        {
            foreach (var (plant, holder) in _lookup.GetEntitiesInRange<PlantHolderComponent>(coordinates, aura.Radius))
            {
                if (holder.Dead || holder.Health >= aura.PlantHealthCap)
                    continue;

                holder.Health = MathF.Min(aura.PlantHealthCap, holder.Health + aura.PlantHealPerSecond);
            }
        }

        if (aura.WeedReducePerSecond > 0)
        {
            foreach (var (tray, trayComponent) in _lookup.GetEntitiesInRange<PlantTrayComponent>(coordinates, aura.Radius))
            {
                if (trayComponent.WeedLevel <= 0)
                    continue;

                _plantTray.AdjustWeed((tray, trayComponent), -aura.WeedReducePerSecond);
            }
        }

        if (!aura.CleanPuddles)
            return;

        // Eris clean_blood() only wipes the bearer's own tile.
        foreach (var (puddle, _) in _lookup.GetEntitiesInRange<PuddleComponent>(coordinates, 0.5f))
            QueueDel(puddle);
    }

    /// <summary>
    /// Spreads one group's healing rate across the damaged types in that group, so a
    /// slash wound receives slash healing instead of blunt healing. The proportional
    /// shares keep the configured total rate and never push a type below zero.
    /// </summary>
    private void AddGroupHeal(DamageSpecifier heal, DamageableComponent damageable,
        ProtoId<DamageGroupPrototype> groupId, float totalHeal)
    {
        if (totalHeal <= 0 || !_prototypes.TryIndex(groupId, out DamageGroupPrototype? group))
            return;

        var damaged = _damageable.GetPositiveDamage(damageable);
        var total = FixedPoint2.Zero;
        foreach (var type in group.DamageTypes)
        {
            if (damaged.DamageDict.TryGetValue(type, out var value) && value > FixedPoint2.Zero)
                total += value;
        }

        if (total <= FixedPoint2.Zero)
            return;

        var rate = FixedPoint2.New(totalHeal);
        foreach (var type in group.DamageTypes)
        {
            if (!damaged.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
                continue;

            var share = rate * (value / total);
            if (share > value)
                share = value;

            heal.DamageDict[type] = heal.DamageDict.GetValueOrDefault(type) - share;
        }
    }

    private void TriggerMartyr(EntityUid body, EntityUid cruciform, CruciformComponent component, EntityUid upgrade,
        CruciformUpgradeMartyrComponent martyr)
    {
        var coordinates = Transform(body).Coordinates;
        var source = _xform.GetWorldPosition(Transform(body));

        // Eris skips every disciple and hits creatures too; only the faithful are spared.
        if (martyr.Radius > 0)
        {
            foreach (var (target, _) in _lookup.GetEntitiesInRange<DamageableComponent>(coordinates, martyr.Radius))
            {
                if (target == body || HasComp<CruciformBearerComponent>(target))
                    continue;

                var distance = MathF.Max(1f, (_xform.GetWorldPosition(Transform(target)) - source).Length());
                var damage = martyr.BurstDamage * (1f / distance);
                _damageable.TryChangeDamage(target, damage, origin: body);
            }
        }

        // Eris deletes the upgrade and keeps the cruciform.
        component.Upgrade = null;
        RemComp<CruciformMartyrArmedComponent>(body);
        _cruciform.RecomputeProfile(cruciform, component);
        QueueDel(upgrade);
    }

    [SubscribeLocalEvent]
    private void OnRefreshMovementSpeed(Entity<MovementSpeedModifierComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!HasComp<CruciformBearerComponent>(ent.Owner))
            return;

        if (!_cruciform.TryGetCruciform(ent.Owner, out _, out var cruciformComp) ||
            cruciformComp.Upgrade is not { } upgrade ||
            !TryComp<CruciformUpgradeSpeedComponent>(upgrade, out var speed))
        {
            return;
        }

        args.ModifySpeed(speed.WalkMultiplier, speed.SprintMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnGetMeleeDamage(Entity<MeleeWeaponComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!HasComp<CruciformBearerComponent>(args.User))
            return;

        if (!_cruciform.TryGetCruciform(args.User, out _, out var cruciformComp) ||
            cruciformComp.Upgrade is not { } upgrade ||
            !TryComp<CruciformUpgradeMeleeComponent>(upgrade, out var melee))
        {
            return;
        }

        args.Damage = args.Damage * (1f + melee.BonusMultiplier);
    }

    /// <summary>Read-only weed reading used by tests to observe the aura output.</summary>
    public float TestingGetWeedLevel(EntityUid tray)
        => TryComp<PlantTrayComponent>(tray, out var component) ? component.WeedLevel : 0f;
}
