using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Ntos;
using Content.Server._Oxyd.Pda;
using Content.Server.DeviceLinking.Components;
using Content.Server.Medical.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared._Oxyd.Ntos;
using Content.Shared._Oxyd.Pda;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.Ntos;

[TestFixture]
public sealed class OxydNtosAndHardwareTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task EmailRegisterSendAndChatChannel()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var email = server.System<OxydEmailSystem>();
            var chat = server.System<OxydNtChatSystem>();

            var a = entMan.SpawnEntity("OxydPdaClub", map.MapCoords);
            var b = entMan.SpawnEntity("OxydPdaArtist", map.MapCoords);

            Assert.That(email.TryRegister("alice", "pw", out var err), Is.True, err);
            Assert.That(email.TryRegister("bob", "pw", out err), Is.True, err);
            Assert.That(email.TryLogin(a, "alice", "pw", out err), Is.True, err);
            Assert.That(email.TrySend("alice@nt.mail", "bob@nt.mail", "Hi", "Hello bob", out err), Is.True, err);

            var inbox = email.GetInbox("bob");
            Assert.That(inbox.Any(m => m.Subject == "Hi" && m.From == "alice@nt.mail"), Is.True);

            chat.GetOrSetName(a, "Alice");
            chat.GetOrSetName(b, "Bob");
            var general = chat.ListChannels().First(c => c.Title == "General");
            Assert.That(chat.TryJoin(a, general.Id, out err), Is.True, err);
            Assert.That(chat.TryJoin(b, general.Id, out err), Is.True, err);
            Assert.That(chat.TrySpeak(a, "ping", out err), Is.True, err);
            Assert.That(chat.GetChannel(general.Id)!.Messages.Any(m => m.Contains("ping")), Is.True);

            entMan.DeleteEntity(a);
            entMan.DeleteEntity(b);
        });
    }

    [Test]
    public async Task SignallerAndMedicalScannerHardwareAttach()
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

            if (slots.GetItemOrNull(pda, OxydModularPdaComponent.PeripheralSlotId) is { } old)
            {
                slots.TryEject(pda, OxydModularPdaComponent.PeripheralSlotId, null, out _);
                entMan.DeleteEntity(old);
            }

            var signal = entMan.SpawnEntity("OxydHwSignaller", map.MapCoords);
            Assert.That(slots.TryInsert(pda, OxydModularPdaComponent.PeripheralSlotId, signal, null), Is.True);
            sys.Recalculate((pda, mod));
            Assert.That(mod.HasSignaller, Is.True);
            Assert.That(entMan.HasComponent<SignallerComponent>(pda), Is.True);
            Assert.That(entMan.HasComponent<DeviceLinkSourceComponent>(pda), Is.True);

            slots.TryEject(pda, OxydModularPdaComponent.PeripheralSlotId, null, out var ejected);
            if (ejected != null)
                entMan.DeleteEntity(ejected.Value);

            var med = entMan.SpawnEntity("OxydHwScannerMedical", map.MapCoords);
            Assert.That(slots.TryInsert(pda, OxydModularPdaComponent.PeripheralSlotId, med, null), Is.True);
            sys.Recalculate((pda, mod));
            Assert.That(mod.ActiveScanner, Is.EqualTo(OxydScannerKind.Medical));
            Assert.That(entMan.HasComponent<HealthAnalyzerComponent>(pda), Is.True);

            entMan.DeleteEntity(pda);
        });
    }

    [Test]
    public async Task PriceScannerWritesBuffer()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var slots = server.System<ItemSlotsSystem>();
            var sys = server.System<OxydModularPdaSystem>();

            var pda = entMan.SpawnEntity("OxydPdaArtist", map.MapCoords);
            var mod = entMan.GetComponent<OxydModularPdaComponent>(pda);

            if (slots.GetItemOrNull(pda, OxydModularPdaComponent.PeripheralSlotId) is { } old)
            {
                slots.TryEject(pda, OxydModularPdaComponent.PeripheralSlotId, null, out _);
                entMan.DeleteEntity(old);
            }

            var priceHw = entMan.SpawnEntity("OxydHwScannerPrice", map.MapCoords);
            Assert.That(slots.TryInsert(pda, OxydModularPdaComponent.PeripheralSlotId, priceHw, null), Is.True);
            sys.Recalculate((pda, mod));
            Assert.That(mod.ActiveScanner, Is.EqualTo(OxydScannerKind.Price));

            var target = entMan.SpawnEntity("OxydArtworkStatue", map.MapCoords);
            var user = entMan.SpawnEntity("MobHuman", map.MapCoords);
            var coords = entMan.GetComponent<TransformComponent>(target).Coordinates;
            var ev = new AfterInteractEvent(user, pda, target, coords, true);
            entMan.EventBus.RaiseLocalEvent(pda, ev);

            Assert.That(mod.ScanBuffer, Does.Contain("PRICE SCAN"));

            entMan.DeleteEntity(user);
            entMan.DeleteEntity(target);
            entMan.DeleteEntity(pda);
        });
    }

    [Test]
    public async Task LiveRecordsAndBountyCartridgesExist()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var records = entMan.SpawnEntity("OxydRecordsCartridge", map.MapCoords);
            var bounty = entMan.SpawnEntity("OxydBountyCartridge", map.MapCoords);
            Assert.That(entMan.HasComponent<OxydRecordsCartridgeComponent>(records), Is.True);
            Assert.That(entMan.HasComponent<OxydBountyCartridgeComponent>(bounty), Is.True);
            Assert.That(entMan.HasComponent<OxydCatalogCartridgeComponent>(records), Is.False);

            entMan.DeleteEntity(records);
            entMan.DeleteEntity(bounty);
        });
    }
}
