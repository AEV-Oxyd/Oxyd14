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
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

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
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    /// <summary>One aura tick per second; Eris's SSobj cadence reduced to a stable unit.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static readonly ProtoId<DamageGroupPrototype> BruteGroup = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> BurnGroup = "Burn";

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickInterval;

        var query = EntityQueryEnumerator<CruciformBearerComponent>();
        while (query.MoveNext(out var body, out _))
        {
            if (!_cruciform.TryGetCruciformEntity(body, out var cruciform, out var component) ||
                component.Upgrade is not { } upgrade)
            {
                continue;
            }

            if (_mobState.IsDead(body))
            {
                // Eris fires the martyr burst from the death hook; a one-second tick is the
                // closest bounded substitute because MobStateChangedEvent is already taken.
                // Death deactivates the cruciform, so the lookup must ignore the active flag.
                if (TryComp<CruciformUpgradeMartyrComponent>(upgrade, out var martyr))
                    TriggerMartyr(body, cruciform, component, upgrade, martyr);

                continue;
            }

            if (!component.Active)
                continue;

            if (TryComp<CruciformUpgradeAuraComponent>(upgrade, out var aura))
                ApplyAura(body, aura);
        }
    }

    private void ApplyAura(EntityUid body, CruciformUpgradeAuraComponent aura)
    {
        var coordinates = Transform(body).Coordinates;

        // Eris heals only wounded faithful and only above the threshold.
        foreach (var (target, damageable) in _lookup.GetEntitiesInRange<DamageableComponent>(coordinates, aura.Radius))
        {
            if (target == body || _mobState.IsDead(target) || !HasComp<CruciformBearerComponent>(target))
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
                if (holder.Dead || holder.Health >= PlantHealthCap)
                    continue;

                holder.Health = MathF.Min(PlantHealthCap, holder.Health + aura.PlantHealPerSecond);
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
        foreach (var (target, _) in _lookup.GetEntitiesInRange<DamageableComponent>(coordinates, martyr.Radius))
        {
            if (target == body || HasComp<CruciformBearerComponent>(target))
                continue;

            var distance = MathF.Max(1f, (_xform.GetWorldPosition(Transform(target)) - source).Length());
            var damage = new DamageSpecifier
            {
                DamageDict = { ["Heat"] = FixedPoint2.New(martyr.Burn / distance) },
            };
            _damageable.TryChangeDamage(target, damage, origin: body);
        }

        // Eris deletes the upgrade and keeps the cruciform.
        component.Upgrade = null;
        _cruciform.RecomputeProfile(cruciform, component);
        QueueDel(upgrade);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, MovementSpeedModifierComponent component,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!HasComp<CruciformBearerComponent>(uid))
            return;

        if (!_cruciform.TryGetCruciform(uid, out _, out var cruciformComp) ||
            cruciformComp.Upgrade is not { } upgrade ||
            !TryComp<CruciformUpgradeSpeedComponent>(upgrade, out var speed))
        {
            return;
        }

        args.ModifySpeed(speed.WalkMultiplier, speed.SprintMultiplier);
    }

    private void OnGetMeleeDamage(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeDamageEvent args)
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

    private const float PlantHealthCap = 100f;

    /// <summary>Read-only weed reading used by tests to observe the aura output.</summary>
    public float TestingGetWeedLevel(EntityUid tray)
        => TryComp<PlantTrayComponent>(tray, out var component) ? component.WeedLevel : 0f;
}
