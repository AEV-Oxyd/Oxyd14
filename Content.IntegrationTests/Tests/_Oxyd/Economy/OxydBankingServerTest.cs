using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Server.Cargo.Systems;
using Content.Shared._Oxyd.Economy;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydBankingServerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task DepartmentsComeFromTheBankingServerPrototype()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var bank = eco.Bank;

            Assert.That(bank.Comp.Departments, Is.Not.Empty);
            foreach (var dept in bank.Comp.Departments)
            {
                var acc = eco.FindDepartmentAccount(dept.Id);
                Assert.That(acc, Is.Not.Null, dept.Id);
                Assert.That(acc!.Employer, Is.EqualTo(dept.Employer));
                Assert.That(acc.Wage, Is.EqualTo(dept.Wage));
                Assert.That(acc.CanMakeAccounts, Is.EqualTo(dept.CanMakeAccounts));
            }

            // The accounts live on the server entity, and there is only one of it.
            var entMan = server.ResolveDependency<IEntityManager>();
            var count = 0;
            var query = entMan.EntityQueryEnumerator<OxydBankingServerComponent>();
            while (query.MoveNext(out _, out _))
                count++;
            Assert.That(count, Is.EqualTo(1));
            Assert.That(eco.Bank.Owner, Is.EqualTo(bank.Owner));
        });
    }

    [Test]
    public async Task AtmStaysLoggedInForTheNextUser()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var eco = server.System<OxydEconomySystem>();
            var atms = server.System<OxydAtmSystem>();

            var acc = eco.CreateAccount("Forgetful", "Forgetful", 500, OxydEconomyConstants.EmployerService, 0);
            var atmUid = entMan.SpawnEntity("OxydAtm", map.MapCoords);
            var atm = new Entity<OxydAtmComponent>(atmUid, entMan.GetComponent<OxydAtmComponent>(atmUid));
            var owner = entMan.SpawnEntity(null, map.MapCoords);
            var stranger = entMan.SpawnEntity(null, map.MapCoords);

            Assert.That(atms.TryLogin(atm, acc.AccountNumber, acc.Pin, owner, out var err), Is.True, err);

            // The owner walked off without logging out; whoever comes next can use the session.
            Assert.That(atms.TryWithdraw(atm, 100, stranger, out err), Is.True, err);
            Assert.That(acc.Balance, Is.EqualTo(400));
            Assert.That(atm.Comp.SessionAccount, Is.EqualTo(acc.AccountNumber));
        });
    }

    [Test]
    public async Task ExportPaysTheLinkedTradeAccount()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var eco = server.System<OxydEconomySystem>();
            var trade = server.System<OxydTradeSystem>();
            var pricing = server.System<PricingSystem>();

            var linked = eco.CreateAccount("Merchant", "Merchant", 0, OxydEconomyConstants.EmployerService, 0);
            var beacon = entMan.SpawnEntity("OxydTradeBeaconSending", map.MapCoords);
            var goods = entMan.SpawnEntity("Crowbar", map.MapCoords);
            var price = (int)Math.Round(pricing.GetPrice(goods));
            Assert.That(price, Is.GreaterThan(0));

            var paid = trade.ExportGoodsNear(beacon, 2f, linked.AccountNumber);
            Assert.That(paid, Is.EqualTo(price));
            Assert.That(linked.Balance, Is.EqualTo(price));
            Assert.That(entMan.IsQueuedForDeletion(goods), Is.True);
        });
    }
}
