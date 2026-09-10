using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.8: the biogenerator is Eris' multistructure flattened into one machine that feeds its
/// rated power into the network while it runs, and nothing once the pipes are fouled.
/// </summary>
[TestOf(typeof(BiogeneratorSystem))]
public sealed class BiogeneratorTest : GameTest
{
    private const float RatedPower = 500_000f;

    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task WorkingGeneratorSuppliesItsRatedPower()
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
            Assert.That(SComp<PowerSupplierComponent>(generator).MaxSupply,
                Is.EqualTo(RatedPower).Within(1f),
                "A working biogenerator must offer its rated power to the network.");
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
        return generator;
    }
}
