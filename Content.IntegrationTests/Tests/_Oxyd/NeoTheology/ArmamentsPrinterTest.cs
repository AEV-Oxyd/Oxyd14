using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Server.Power.Components;
using Content.Server.Lathe;
using Content.Shared._Oxyd;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.16: the armaments printer only sells to an active bearer of a configured profile, only within
/// reach, and only when the armory can pay — and it debits exactly what it charged. P3.5 rebases the
/// points onto the Eye of the Protector, so the sale debits the Eye, not the printer.
/// </summary>
[TestOf(typeof(ArmamentsPrinterSystem))]
public sealed class ArmamentsPrinterTest : GameTest
{
    private static readonly EntProtoId PrinterProto = "OxydNtArmamentsPrinter";
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId ArmamentPath = "OxydNtRitualBookDesignDisk";
    private const string ArmamentId = "OxydNtArmamentRitualBook";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly ArmamentsPrinterSystem _printer = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task APurchaseWithoutPointsSpawnsNothing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var printer = SSpawnAtPosition(PrinterProto, map.GridCoords);
            var eye = SpawnEye(map.GridCoords);
            var buyer = ActiveBearer(map.GridCoords);

            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).ArmamentsPoints, Is.Zero,
                "Setup: a fresh Eye must start with no armament points.");

            Assert.That(_printer.TryPurchase(printer, buyer, ArmamentId), Is.False,
                "An empty armory must refuse the sale.");
            Assert.That(CountArmaments(), Is.Zero, "A refused sale must not spawn anything.");
        });
    }

    [Test]
    public async Task APurchaseWithPointsSpawnsOnThePrinterAndDebitsTheEye()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var printer = SSpawnAtPosition(PrinterProto, map.GridCoords);
            var eye = SpawnEye(map.GridCoords);
            SComp<ApcPowerReceiverComponent>(printer).Powered = true;
            Assert.That(SComp<TransformComponent>(printer).Anchored, Is.True);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            var buyer = ActiveBearer(map.GridCoords);
            var cost = _printer.GetCost(eyeComp, SProtoMan.Index<ArmamentPrototype>(ArmamentId));
            eyeComp.ArmamentsPoints = cost + 10;

            Assert.That(_printer.TryPurchase(printer, buyer, ArmamentId), Is.True,
                "A stocked armory must sell to a faithful buyer in reach.");
            Assert.That(eyeComp.ArmamentsPoints, Is.EqualTo(10), "The sale must debit the Eye exactly its price.");

            var spawned = EntitiesOfPrototype(ArmamentPath).Single();
            Assert.That(SComp<TransformComponent>(spawned).Coordinates,
                Is.EqualTo(SComp<TransformComponent>(printer).Coordinates),
                "The armament must appear on the printer's turf.");
        });
    }

    [TestCase("OxydNtArmamentRitualBook", "OxydNtBible")]
    [TestCase("OxydNtArmamentEnergyCrossbow", "WeaponEnergyCrossbow")]
    [TestCase("OxydNtArmamentHolyGrenade", "OxydNtHolyHandGrenade")]
    public async Task PurchasedDiskUnlocksExactlyOneLathePrint(string armamentId, string result)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var printer = SSpawnAtPosition(PrinterProto, map.GridCoords);
            SComp<ApcPowerReceiverComponent>(printer).Powered = true;
            var eye = SpawnEye(map.GridCoords);
            var buyer = ActiveBearer(map.GridCoords);
            var armament = SProtoMan.Index<ArmamentPrototype>(armamentId);
            SComp<EyeOfTheProtectorComponent>(eye).ArmamentsPoints = armament.Cost;
            Assert.That(_printer.TryPurchase(printer, buyer, armamentId), Is.True);
            var disk = EntitiesOfPrototype(armament.Path).Single();
            Assert.That(EntitiesOfPrototype(result), Is.Empty);
            var file = SComp<DigitalDataHolderComponent>(disk).getFileByData<DigitalDataLathe>().Single();
            var recipe = SProtoMan.Index(file.recipes.Single());
            Assert.That(recipe.Result?.Id, Is.EqualTo(result));

            var lathe = SSpawnAtPosition("OxydLathe", map.GridCoords);
            SComp<ApcPowerReceiverComponent>(lathe).Powered = true;
            var component = SComp<LatheComponent>(lathe);
            var originalMaterials = SComp<MaterialStorageComponent>(lathe).MaterialWhiteList;
            component.TimeMultiplier = 0;
            component.MaterialUseMultiplier = 1;
            var system = SEntMan.System<LatheSystem>();
            Assert.That(system.GetAvailableRecipes(lathe, component), Does.Not.Contain(new ProtoId<LatheRecipePrototype>(recipe.ID)));
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(lathe, SharedLatheSystem.diskSlot, disk, null), Is.True);
            Assert.That(system.GetAvailableRecipes(lathe, component), Does.Contain(new ProtoId<LatheRecipePrototype>(recipe.ID)));
            foreach (var (material, amount) in recipe.Materials)
                Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(lathe, material, amount * 2), Is.True);

            Assert.That(system.TryAddToQueue(lathe, recipe, 2), Is.False);
            Assert.That(file.uses, Is.EqualTo(1));
            Assert.That(system.TryAddToQueue(lathe, recipe, 1), Is.True);
            Assert.That(file.uses, Is.Zero);
            Assert.That(system.TryAddToQueue(lathe, recipe, 1), Is.False);
            Assert.That(system.TryStartProducing(lathe), Is.True);
            Assert.That(EntitiesOfPrototype(result), Has.Count.EqualTo(1));
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryEject(lathe, SharedLatheSystem.diskSlot, null, out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(disk));
            Assert.That(system.GetAvailableRecipes(lathe, component), Does.Not.Contain(new ProtoId<LatheRecipePrototype>(recipe.ID)));
            Assert.That(SComp<MaterialStorageComponent>(lathe).MaterialWhiteList, Is.EquivalentTo(originalMaterials));
        });
    }

    private EntityUid SpawnEye(EntityCoordinates coords)
    {
        // ponytail: the Eye prototype arrives in P3.7; until then the printer debits a bare entity.
        var eye = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<EyeOfTheProtectorComponent>(eye);
        return eye;
    }

    private EntityUid ActiveBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: the cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be an active follower.");
        return body;
    }

    private List<EntityUid> EntitiesOfPrototype(EntProtoId prototype)
    {
        var found = new List<EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype.Id)
                found.Add(uid);
        }

        return found;
    }

    private int CountArmaments() => EntitiesOfPrototype(ArmamentPath).Count;
}
