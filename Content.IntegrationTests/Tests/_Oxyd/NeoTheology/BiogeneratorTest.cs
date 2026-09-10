using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.8/P2.10a: the biogenerator is Eris' multistructure flattened into one machine. It burns
/// biomatter from its MaterialStorage for power and supplies nothing once the store runs dry.
/// </summary>
[TestOf(typeof(BiogeneratorSystem))]
public sealed class BiogeneratorTest : GameTest
{
    private const float RatedPower = 500_000f;
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _material = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;

    [Test]
    public async Task StockedGeneratorSuppliesPowerAndBurnsBiomatter()
    {
        var map = await Pair.CreateTestMap();
        EntityUid generator = default;

        await Server.WaitAssertion(() =>
        {
            generator = SpawnBiogenerator(map.GridCoords);
            Stock(generator, map.GridCoords, 10);
            SComp<BiogeneratorComponent>(generator).Working = true;
        });

        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SComp<PowerSupplierComponent>(generator).MaxSupply,
                    Is.EqualTo(RatedPower).Within(1f),
                    "A working biogenerator must offer its rated power to the network.");
                Assert.That(_material.GetMaterialAmount(generator, "Biomatter"), Is.LessThan(10),
                    "A working biogenerator must burn biomatter as it supplies power.");
            });
        });
    }

    [Test]
    public async Task DryGeneratorSuppliesNothing()
    {
        var map = await Pair.CreateTestMap();
        EntityUid generator = default;

        await Server.WaitAssertion(() =>
        {
            generator = SpawnBiogenerator(map.GridCoords);
            SComp<BiogeneratorComponent>(generator).Working = true;
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SComp<PowerSupplierComponent>(generator).MaxSupply,
                    Is.EqualTo(0f).Within(1f),
                    "A biogenerator with no biomatter must supply nothing.");
                Assert.That(SComp<PowerSupplierComponent>(generator).Enabled, Is.False,
                    "A biogenerator with no biomatter must not claim to be supplying.");
            });
        });
    }

    [Test]
    public async Task FouledGeneratorSuppliesNothing()
    {
        var map = await Pair.CreateTestMap();
        EntityUid generator = default;

        await Server.WaitAssertion(() =>
        {
            generator = SpawnBiogenerator(map.GridCoords);
            Stock(generator, map.GridCoords, 10);
            var comp = SComp<BiogeneratorComponent>(generator);
            comp.Working = true;
            comp.Dirtiness = 1f;
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PowerSupplierComponent>(generator).MaxSupply,
                Is.EqualTo(0f).Within(1f),
                "A fully fouled biogenerator must stop supplying power.");
        });
    }

    private EntityUid SpawnBiogenerator(EntityCoordinates coords)
    {
        var generator = SSpawnAtPosition(null, coords);
        var comp = SEntMan.AddComponent<BiogeneratorComponent>(generator);
        comp.OutputWatts = RatedPower;
        SEntMan.AddComponent<PowerSupplierComponent>(generator);
        SEntMan.AddComponent<MaterialStorageComponent>(generator);
        return generator;
    }

    private void Stock(EntityUid generator, EntityCoordinates coords, int count)
    {
        var items = SSpawnAtPosition(BiomatterProto, coords);
        _stack.SetCount((Entity<StackComponent?>) items, count);

        Assert.That(_material.TryInsertMaterialEntity(generator, items, generator), Is.True,
            $"Setup: banks {count} biomatter in the biogenerator.");
    }
}
