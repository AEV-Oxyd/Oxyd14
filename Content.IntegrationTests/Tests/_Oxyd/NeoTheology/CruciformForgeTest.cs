using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.6: the cruciform forge banks the recipe materials (through its MaterialStorage) and prints
/// a cruciform through its headless <see cref="LatheComponent"/> once the litany queues the recipe.
/// </summary>
[TestOf(typeof(CruciformForgeSystem))]
public sealed class CruciformForgeTest : GameTest
{
    private static readonly EntProtoId PlasteelProto = "SheetPlasteel";
    private static readonly EntProtoId GoldProto = "IngotGold";
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId ForgeProto = "OxydNtCruciformForge";

    /// <summary>Where the forge sits relative to the test tile.</summary>
    private static readonly Vector2 ForgeOffset = new(3f, 0f);

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CruciformForgeSystem _forge = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _material = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task StockedForgeStartsPrintAndSpendsTheRecipe()
    {
        var map = await Pair.CreateMachineTestMap();

        await Server.WaitAssertion(() =>
        {
            var (forge, _) = SpawnForge(map.GridCoords);
            InsertRecipe(forge, map.GridCoords);

            Assert.That(_forge.TryProduce(forge), Is.True,
                "A forge stocked with the full recipe must accept a produce run.");

            var lathe = SComp<LatheComponent>(forge);
            Assert.Multiple(() =>
            {
                Assert.That(lathe.CurrentRecipe?.Id, Is.EqualTo(CruciformProto.Id),
                    "The lathe must be mid-print once the run starts.");
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
    public async Task FinishedPrintLeavesACruciformOnTheTurf()
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
            var lathe = SComp<LatheComponent>(forge);
            var coords = SEntMan.GetComponent<TransformComponent>(forge).Coordinates;

            Assert.Multiple(() =>
            {
                Assert.That(lathe.CurrentRecipe, Is.Null, "The print must be over after CompleteTime.");
                Assert.That(lathe.Queue, Is.Empty, "Nothing may remain queued after the print.");
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
            var (forge, _) = SpawnForge(map.GridCoords);

            Insert(forge, map.GridCoords, BiomatterProto, 10);
            Insert(forge, map.GridCoords, PlasteelProto, 5);
            // no gold

            Assert.That(_forge.TryProduce(forge), Is.False,
                "A forge missing one recipe material must refuse the run.");

            var lathe = SComp<LatheComponent>(forge);
            Assert.Multiple(() =>
            {
                Assert.That(lathe.CurrentRecipe, Is.Null, "A refused run must not start printing.");
                Assert.That(lathe.Queue, Is.Empty, "A refused run must not queue anything.");
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
                Assert.That(SEntMan.HasComponent<LatheComponent>(forge), Is.True,
                    "The forge machine must carry a lathe for production.");
                Assert.That(SEntMan.HasComponent<MaterialStorageComponent>(forge), Is.True,
                    "The forge machine must take material storage.");
                Assert.That(SEntMan.HasComponent<ApcPowerReceiverComponent>(forge), Is.True,
                    "The forge machine must draw power.");
            });
        });
    }

    [Test]
    public async Task CruciformIsVisibleInTheSpawnMenuAndIsAnItem()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = _prototypes.Index<EntityPrototype>(CruciformProto);

            Assert.Multiple(() =>
            {
                Assert.That(prototype.HideSpawnMenu, Is.False,
                    "The cruciform must appear in the spawn menu.");
                Assert.That(prototype.Components.ContainsKey("Item"), Is.True,
                    "The cruciform must be a holdable item.");
            });
        });
    }

    [Test]
    public async Task ValidationDoesNotSpendAndPowerBlocksTheQueue()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var (forge, _) = SpawnForge(map.GridCoords);
            InsertRecipe(forge, map.GridCoords);
            var receiver = SComp<ApcPowerReceiverComponent>(forge);
            var validation = new LitanyForgeProduceEvent(forge, false, true);
            SEntMan.EventBus.RaiseLocalEvent(forge, ref validation);
            Assert.That(validation.Handled, Is.True);
            Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(10),
                "A validate-only produce must not debit materials.");

            receiver.Powered = false;
            Assert.That(_forge.TryProduce(forge), Is.False,
                "An unpowered forge must refuse the run.");
            Assert.That(_material.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(10),
                "A refused run must not debit materials.");

            receiver.Powered = true;
            Assert.That(_forge.TryProduce(forge), Is.True);
            Assert.That(_forge.CanProduce(forge), Is.False,
                "A busy forge must refuse another job.");
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
