using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Installed-upgrade behaviours: speed, martyr burst, nature blessing aura and wrath melee
/// bonus. The four stat deltas stay in <see cref="CruciformUpgradeComponent"/> and are covered
/// by the profile tests.
/// </summary>
[TestOf(typeof(CruciformUpgradeBehaviorSystem))]
public sealed class CruciformUpgradeBehaviorTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId SpeedUpgrade = "OxydNtUpgradeSpeedOfTheChosen";
    private static readonly EntProtoId MartyrUpgrade = "OxydNtUpgradeMartyrGift";
    private static readonly EntProtoId AuraUpgrade = "OxydNtUpgradeNaturesBlessing";
    private static readonly EntProtoId WrathUpgrade = "OxydNtUpgradeWrathOfGod";
    private static readonly EntProtoId TrayProto = "HydroponicsTrayEmpty";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeBehaviorSystem _behavior = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly SharedMeleeWeaponSystem _melee = default!;

    [Test]
    public async Task SpeedUpgrade_DoublesMovementSpeedWhileInstalled()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var body = SpawnBearer(origin);
            var movement = SComp<MovementSpeedModifierComponent>(body);
            var baseWalk = movement.BaseWalkSpeed;
            var baseSprint = movement.BaseSprintSpeed;

            var item = Install(body, SpeedUpgrade);
            movement = SComp<MovementSpeedModifierComponent>(body);
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(baseWalk * 2f).Within(0.01f),
                "Speed of the chosen must double the walk speed while installed.");
            Assert.That(movement.CurrentSprintSpeed, Is.EqualTo(baseSprint * 2f).Within(0.01f),
                "Speed of the chosen must double the sprint speed while installed.");

            Assert.That(_cruciform.TryGetCruciform(body, out var cruciform, out var component), Is.True);
            Assert.That(_upgrades.TryUninstallUpgrade(cruciform, component), Is.True);
            movement = SComp<MovementSpeedModifierComponent>(body);
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(baseWalk).Within(0.01f),
                "Uninstalling the speed upgrade must restore the base speed.");
            Assert.That(SEntMan.EntityExists(item), Is.True);
        });
    }

    [Test]
    public async Task MartyrGift_BurstsOnDeathAndSparesTheFaithful()
    {
        var map = await Pair.CreateTestMap();
        EntityUid item = default;
        EntityUid bystander = default;
        EntityUid faithful = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var bearer = SpawnBearer(origin);
            bystander = SpawnBearer(origin.Offset(new Vector2(2f, 0f)), cruciform: false);
            faithful = SpawnBearer(origin.Offset(new Vector2(-2f, 0f)));
            item = Install(bearer, MartyrUpgrade);

            _mobState.ChangeMobState(bearer, MobState.Dead);
        });

        await AdvanceUntil(
            () => !SEntMan.EntityExists(item),
            "The martyr gift must destroy itself after the burst.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(_damageable.GetTotalDamage(bystander), Is.GreaterThan(FixedPoint2.Zero),
                "The martyr burst must burn a non-cruciformed creature nearby.");
            Assert.That(_damageable.GetTotalDamage(faithful), Is.EqualTo(FixedPoint2.Zero),
                "The martyr burst must spare the faithful.");
        });
    }

    [Test]
    public async Task NaturesBlessing_HealsWoundedFaithfulAndWithersWeeds()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wounded = default;
        EntityUid tray = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var bearer = SpawnBearer(origin);
            wounded = SpawnBearer(origin.Offset(new Vector2(2f, 0f)));
            tray = SSpawnAtPosition(TrayProto, origin.Offset(new Vector2(1f, 0f)));

            Install(bearer, AuraUpgrade);
            _damageable.TryChangeDamage(wounded, new DamageSpecifier
            {
                DamageDict = { ["Blunt"] = FixedPoint2.New(60) },
            });

            var trayComponent = SComp<PlantTrayComponent>(tray);
            SEntMan.System<PlantTraySystem>().AdjustWeed((tray, trayComponent), 10f);
            Assert.That(_behavior.TestingGetWeedLevel(tray), Is.EqualTo(10f).Within(0.01f));
        });

        await AdvanceUntil(
            () => _behavior.TestingGetWeedLevel(tray) < 10f,
            "Nature's blessing must reduce the weed level of trays in range.");

        await AdvanceUntil(
            () =>
            {
                var groups = _damageable.GetDamagePerGroup(wounded);
                return groups.GetValueOrDefault(new ProtoId<DamageGroupPrototype>("Brute")) < FixedPoint2.New(60);
            },
            "Nature's blessing must heal a wounded faithful above the threshold.");
    }

    [Test]
    public async Task WrathOfGod_AddsBonusMeleeDamage()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var bearer = SpawnBearer(origin);
            var weapon = SSpawnAtPosition("Crowbar", origin.Offset(new Vector2(1f, 0f)));
            var baseDamage = _melee.GetDamage(weapon, bearer);
            Install(bearer, WrathUpgrade);

            var boosted = _melee.GetDamage(weapon, bearer);
            Assert.That(boosted.GetTotal(), Is.GreaterThan(baseDamage.GetTotal()),
                "Wrath of god must add a melee bonus for its bearer.");
            Assert.That(boosted.GetTotal(), Is.EqualTo(baseDamage.GetTotal() * (1f + 0.2f)),
                "Wrath of god adds 20% of the base damage.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A living human, optionally with an active cruciform.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords, bool cruciform = true)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
        SEntMan.RemoveComponent<BarotraumaComponent>(body);

        if (!cruciform)
            return body;

        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        return body;
    }

    /// <summary>Installs the given upgrade item into the bearer's cruciform slot.</summary>
    private EntityUid Install(EntityUid body, EntProtoId upgrade)
    {
        Assert.That(_cruciform.TryGetCruciform(body, out var cruciform, out var component), Is.True);
        var item = SSpawnAtPosition(upgrade, SComp<TransformComponent>(body).Coordinates);
        Assert.That(_upgrades.TryInstallUpgrade(cruciform, component, item), Is.True);
        return item;
    }

    /// <summary>Runs ticks until the condition is true on the server thread.</summary>
    private async Task AdvanceUntil(Func<bool> condition, string message)
    {
        for (var i = 0; i < 200; i++)
        {
            await Pair.RunTicksSync(5);
            var done = false;
            await Server.WaitPost(() => done = condition());
            if (done)
                return;
        }

        Assert.Fail(message);
    }
}
