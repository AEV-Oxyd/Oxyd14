using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Cargo.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydAccountsDbAndArtTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task AccountsDbCreateSuspendAdjust()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var cargo = eco.CreateAccount("Cargo Department", "Cargo", 5000,
                null, 0, true, OxydEconomyConstants.EmployerCargo, canMakeAccounts: true);

            Assert.That(eco.TryCreateFundedAccount("HopHire", 500, cargo.AccountNumber, "test",
                out var created, out var err), Is.True, err);
            Assert.That(created, Is.Not.Null);
            Assert.That(created!.Balance, Is.EqualTo(500));
            Assert.That(cargo.Balance, Is.EqualTo(4500));

            Assert.That(eco.TrySetSuspended(created.AccountNumber, true), Is.True);
            Assert.That(eco.TryTransfer(created.AccountNumber, cargo.AccountNumber, 10, "x", "t", out _), Is.False);
            Assert.That(eco.TrySilentAdjust(created.AccountNumber, 50, "silent", "AccountsDB"), Is.True);
            Assert.That(created.Balance, Is.EqualTo(550));

            Assert.That(eco.TryRevokePayroll(created.AccountNumber, cargo.AccountNumber, "AccountsDB", out _), Is.True);
            Assert.That(created.Balance, Is.EqualTo(0));
            Assert.That(cargo.Balance, Is.EqualTo(5050));
        });
    }

    [Test]
    public async Task ArtistBenchConsumesInsightAndPricesArt()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var artist = entMan.SpawnEntity("MobHuman", map.MapCoords);
            var sanity = entMan.EnsureComponent<SanityComponent>(artist);
            sanity.Insight = 80f;

            var benchUid = entMan.SpawnEntity("OxydArtistBench", map.MapCoords);
            var bench = (benchUid, entMan.GetComponent<OxydArtistBenchComponent>(benchUid));
            var sys = server.System<OxydArtistBenchSystem>();

            Assert.That(sys.TryCreateArtwork(bench, artist, 50f, out var err, out var price, out var used), Is.True, err);
            Assert.That(used, Is.EqualTo(50f).Within(0.01f));
            Assert.That(price, Is.EqualTo(50 + 100)); // BasePrice 50 + 50*2
            Assert.That(sanity.Insight, Is.EqualTo(30f).Within(0.01f));

            EntityUid? found = null;
            var query = entMan.EntityQueryEnumerator<OxydArtworkComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                found = uid;
                break;
            }

            Assert.That(found, Is.Not.Null);
            Assert.That(entMan.GetComponent<StaticPriceComponent>(found!.Value).Price, Is.EqualTo(price));
            Assert.That(entMan.GetComponent<OverrideSellComponent>(found.Value).OverrideAccount.Id,
                Is.EqualTo(OxydEconomyConstants.EmployerGuild));

            entMan.DeleteEntity(found.Value);
            entMan.DeleteEntity(benchUid);
            entMan.DeleteEntity(artist);
        });
    }
}
