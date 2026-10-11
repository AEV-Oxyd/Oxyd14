using Content.IntegrationTests.Fixtures;
using Content.Server.Medical.BiomassReclaimer;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

[TestOf(typeof(BiomassReclaimerSystem))]
public sealed class BiomatterReclaimerTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("BiomassReclaimer", "Biomass")]
    [TestCase("OxydNtBiomatterReclaimer", "Biomatter")]
    public async Task ReclaimerProducesOnlyItsConfiguredMaterial(string prototype, string material)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SSpawnAtPosition(prototype, map.GridCoords);
            var reclaimer = SComp<BiomassReclaimerComponent>(uid);
            Assert.That(reclaimer.OutputMaterial.Id, Is.EqualTo(material));
            var lookup = SEntMan.System<EntityLookupSystem>();
            var previousStacks = lookup.GetEntitiesInRange<StackComponent>(map.GridCoords, 1);
            reclaimer.CurrentExpectedYield = 10;
            reclaimer.ProcessingTimer = 0;
            SEntMan.AddComponent<ActiveBiomassReclaimerComponent>(uid);
            SEntMan.System<BiomassReclaimerSystem>().Update(0);
            var stacks = lookup.GetEntitiesInRange<StackComponent>(map.GridCoords, 1);
            stacks.ExceptWith(previousStacks);
            Assert.That(stacks, Is.Not.Empty);
            foreach (var stack in stacks)
                Assert.That(SComp<StackComponent>(stack).StackTypeId.Id, Is.EqualTo(material));
        });
    }
}
