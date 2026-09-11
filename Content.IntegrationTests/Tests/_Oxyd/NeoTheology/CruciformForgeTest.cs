using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.6: the cruciform forge banks the recipe materials (through its MaterialStorage) and produces
/// a cruciform after WorkTime.
/// </summary>
[TestOf(typeof(CruciformForgeSystem))]
public sealed class CruciformForgeTest : GameTest
{
    private static readonly EntProtoId PlasteelProto = "SheetPlasteel";
    private static readonly EntProtoId GoldProto = "IngotGold";
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";
    private static readonly EntProtoId ForgeProto = "OxydNtCruciformForge";

    /// <summary>Where the forge sits relative to the test tile.</summary>
    private static readonly Vector2 ForgeOffset = new(3f, 0f);

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CruciformForgeSystem _forge = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _material = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;

    [Test]
    public async Task StockedForgeStartsWorkAndSpendsTheRecipe()
    {
        var map = await Pair.CreateMachineTestMap();

        await Server.WaitAssertion(() =>
        {
            var (forge, comp) = SpawnForge(map.GridCoords);
            InsertRecipe(forge, map.GridCoords);

            Assert.That(comp.Ready, Is.False, "A forge that has not produced anything is not ready.");
            Assert.That(comp.Working, Is.False, "A freshly stocked forge must not be working yet.");

            Assert.That(_forge.TryProduce(forge, comp), Is.True,
                "A forge stocked with the full recipe must accept a produce run.");

            Assert.Multiple(() =>
            {
                Assert.That(comp.Working, Is.True, "The forge must be working once the run starts.");
                Assert.That(comp.Ready, Is.False, "Nothing is ready to take while the run is in flight.");
                Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(0),
                    "The run must debit 10 biomatter.");
                Assert.That(_material.GetMaterialAmount(forge, "Plasteel"), Is.EqualTo(0),
                    "The run must debit 5 plasteel.");
                Assert.That(_material.GetMaterialAmount(forge, "Gold"), Is.EqualTo(0),
                    "The run must debit 2 gold.");
            });
        });
    }

    [Test]
    public async Task FinishedRunLeavesACruciformOnTheTurf()
    {
        var map = await Pair.CreateMachineTestMap();

        EntityUid forge = default;

        await Server.WaitAssertion(() =>
        {
            (forge, _) = SpawnForge(map.GridCoords);
            InsertRecipe(forge, map.GridCoords);

            Assert.That(_forge.TryProduce(forge), Is.True, "Setup: the stocked forge must start a run.");
        });

        await RunSeconds(31f);

        await Server.WaitAssertion(() =>
        {
            var comp = SComp<CruciformForgeComponent>(forge);
            var coords = SEntMan.GetComponent<TransformComponent>(forge).Coordinates;

            Assert.Multiple(() =>
            {
                Assert.That(comp.Working, Is.False, "The run must be over after WorkTime.");
                Assert.That(comp.Ready, Is.True, "A finished run must leave a cruciform to take.");
                Assert.That(SEntMan.System<EntityLookupSystem>().GetEntitiesInRange<CruciformComponent>(coords, 1f),
                    Is.Not.Empty, "The forged cruciform must be lying on the forge's turf.");
            });
        });
    }

    [Test]
    public async Task PartialStockRefusesTheRunAndDebitsNothing()
    {
        var map = await Pair.CreateMachineTestMap();

        await Server.WaitAssertion(() =>
        {
            var (forge, comp) = SpawnForge(map.GridCoords);

            Insert(forge, map.GridCoords, BiomatterProto, 10);
            Insert(forge, map.GridCoords, PlasteelProto, 5);
            // no gold

            Assert.That(_forge.TryProduce(forge, comp), Is.False,
                "A forge missing one recipe material must refuse the run.");
            Assert.Multiple(() =>
            {
                Assert.That(comp.Working, Is.False, "A refused run must not start working.");
                Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(10),
                    "A refused run must not debit biomatter.");
                Assert.That(_material.GetMaterialAmount(forge, "Plasteel"), Is.EqualTo(500),
                    "A refused run must not debit plasteel.");
            });
        });
    }

    [Test]
    public async Task ForgePrototypeLoadsAsAMachine()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var forge = SSpawnAtPosition(ForgeProto, map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<CruciformForgeComponent>(forge), Is.True,
                    "The forge machine must carry the forge component.");
                Assert.That(SEntMan.HasComponent<MaterialStorageComponent>(forge), Is.True,
                    "The forge machine must take material storage.");
                Assert.That(SEntMan.HasComponent<ApcPowerReceiverComponent>(forge), Is.True,
                    "The forge machine must draw power.");
            });
        });
    }

    [Test]
    public async Task ValidationDoesNotSpendAndPowerLossPausesWork()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var (forge, comp) = SpawnForge(map.GridCoords);
            InsertRecipe(forge, map.GridCoords);
            var receiver = SComp<ApcPowerReceiverComponent>(forge);
            var validation = new LitanyForgeProduceEvent(forge, false, true);
            SEntMan.EventBus.RaiseLocalEvent(forge, ref validation);
            Assert.That(validation.Handled, Is.True);
            Assert.That(comp.Working, Is.False);
            Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(10));
            receiver.Powered = false;
            Assert.That(_forge.TryProduce(forge), Is.False);
            Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(10));

            receiver.Powered = true;
            Assert.That(_forge.TryProduce(forge), Is.True);
            Assert.That(_forge.CanProduce(forge), Is.False, "A busy forge must refuse another job.");
            var started = comp.StartedAt;
            receiver.Powered = false;
            _forge.Update(40);
            Assert.That(comp.StartedAt, Is.EqualTo(started + TimeSpan.FromSeconds(40)));
            Assert.That(comp.Working, Is.True);
            receiver.Powered = true;
            _forge.Update(0);
            Assert.That(comp.Working, Is.True, "Restoring power must not finish paused work.");
            comp.StartedAt = SGameTiming.CurTime - comp.WorkTime;
            _forge.Update(0);
            Assert.That(comp.Ready, Is.True);
        });
    }

    /// <summary>Spawns the real forge prototype, so material insertion goes through MaterialStorage.</summary>
    private (EntityUid Forge, CruciformForgeComponent Comp) SpawnForge(EntityCoordinates coords)
    {
        var forge = SSpawnAtPosition(ForgeProto, coords.Offset(ForgeOffset));
        // MaterialStorageSystem refuses insertion into an unpowered ApcPowerReceiver; there is no APC
        // in PsDisconnected, so mark it powered for the test.
        SComp<ApcPowerReceiverComponent>(forge).NeedsPower = false;
        SComp<ApcPowerReceiverComponent>(forge).Powered = true;
        Assert.That(SComp<TransformComponent>(forge).Anchored, Is.True);
        return (forge, SComp<CruciformForgeComponent>(forge));
    }

    private void InsertRecipe(EntityUid forge, EntityCoordinates coords)
    {
        // Recipe is in material volume units: biomatter 10/sheet, plasteel & gold 100/sheet.
        Insert(forge, coords, BiomatterProto, 10);
        Insert(forge, coords, PlasteelProto, 5);
        Insert(forge, coords, GoldProto, 2);
    }

    /// <summary>Banks a stack in the forge through the real MaterialStorage insertion path.</summary>
    private void Insert(EntityUid forge, EntityCoordinates coords, EntProtoId proto, int count)
    {
        var items = SSpawnAtPosition(proto, coords);
        _stack.SetCount((Entity<StackComponent?>) items, count);

        Assert.That(_material.TryInsertMaterialEntity(forge, items, forge), Is.True,
            $"Setup: banks {count} {proto} in the forge.");
    }
}
