using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Pda;
using Content.Shared._Oxyd.Pda;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.Pda;

[TestFixture]
public sealed class OxydModularPdaHardwareTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task ClubPdaStartsWithModularHardwareAndDiskSpace()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var pda = entMan.SpawnEntity("OxydPdaClub", map.MapCoords);

            Assert.That(entMan.HasComponent<OxydModularPdaComponent>(pda), Is.True);
            Assert.That(entMan.TryGetComponent(pda, out CartridgeLoaderComponent? loader), Is.True);

            var mod = entMan.GetComponent<OxydModularPdaComponent>(pda);
            var sys = server.System<OxydModularPdaSystem>();
            sys.Recalculate((pda, mod));

            // SmallAdv HDD = 12
            Assert.That(mod.EffectiveDiskSpace, Is.EqualTo(12));
            Assert.That(loader!.DiskSpace, Is.EqualTo(12));
            Assert.That(mod.EffectiveMaxPrograms, Is.EqualTo(2)); // small CPU
            Assert.That(mod.HasPrinter, Is.True);

            var slots = server.System<ItemSlotsSystem>();
            Assert.That(slots.GetItemOrNull(pda, OxydModularPdaComponent.HddSlotId), Is.Not.Null);
            Assert.That(slots.GetItemOrNull(pda, OxydModularPdaComponent.CpuSlotId), Is.Not.Null);
            Assert.That(slots.GetItemOrNull(pda, OxydModularPdaComponent.NetSlotId), Is.Not.Null);

            entMan.DeleteEntity(pda);
        });
    }

    [Test]
    public async Task UpgradingHddIncreasesDiskSpace()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var slots = server.System<ItemSlotsSystem>();
            var sys = server.System<OxydModularPdaSystem>();

            var pda = entMan.SpawnEntity("OxydPdaChurch", map.MapCoords);
            var mod = entMan.GetComponent<OxydModularPdaComponent>(pda);
            sys.Recalculate((pda, mod));
            var before = mod.EffectiveDiskSpace;

            Assert.That(slots.TryEject(pda, OxydModularPdaComponent.HddSlotId, null, out var oldHdd), Is.True);
            if (oldHdd != null)
                entMan.DeleteEntity(oldHdd.Value);

            var adv = entMan.SpawnEntity("OxydHwHddAdvanced", map.MapCoords);
            Assert.That(slots.TryInsert(pda, OxydModularPdaComponent.HddSlotId, adv, null), Is.True);

            sys.Recalculate((pda, mod));
            Assert.That(mod.EffectiveDiskSpace, Is.EqualTo(16));
            Assert.That(mod.EffectiveDiskSpace, Is.GreaterThan(before));
            Assert.That(entMan.GetComponent<CartridgeLoaderComponent>(pda).DiskSpace, Is.EqualTo(16));

            entMan.DeleteEntity(pda);
        });
    }
}
