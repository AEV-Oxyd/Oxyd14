using Content.IntegrationTests.Fixtures;
using Content.Server.Medical.BiomassReclaimer;
using Content.Shared.Medical.BiomassReclaimer;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

[TestOf(typeof(ServerBiomassReclaimerSystem))]
public sealed class BiomatterReclaimerTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("BiomassReclaimer", "Biomass")]
    [TestCase("OxydNtBiomatterReclaimer", "Biomatter")]
    public async Task ReclaimerIsConfiguredForItsMaterial(string prototype, string material)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SSpawnAtPosition(prototype, map.GridCoords);
            var reclaimer = SComp<BiomassReclaimerComponent>(uid);
            Assert.That(reclaimer.OutputMaterial.Id, Is.EqualTo(material));
        });
    }
}
