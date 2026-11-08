using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Shared._Oxyd.Economy;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydMoneyAccountTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task CreateTransferAndSuspend()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();

            var a = eco.CreateAccount("Alice", "Alice", 1000, OxydEconomyConstants.EmployerService, 0);
            var b = eco.CreateAccount("Bob", "Bob", 100, OxydEconomyConstants.EmployerService, 0);

            Assert.That(a.AccountNumber, Is.Not.EqualTo(b.AccountNumber));
            Assert.That(eco.TryLogin(a.AccountNumber, a.Pin, out _), Is.True);
            Assert.That(eco.TryLogin(a.AccountNumber, a.Pin + 1, out _), Is.False);

            Assert.That(eco.TryTransfer(a.AccountNumber, b.AccountNumber, 250, "test", "test", out var err), Is.True);
            Assert.That(err, Is.Null);
            Assert.That(a.Balance, Is.EqualTo(750));
            Assert.That(b.Balance, Is.EqualTo(350));

            a.Suspended = true;
            Assert.That(eco.TryTransfer(a.AccountNumber, b.AccountNumber, 10, "test", "test", out _), Is.False);
        });
    }

    [Test]
    public async Task PaydaySkipsWageNone()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var manager = eco.CreateAccount("Manager", "Manager", 4000, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageNone);
            var worker = eco.CreateAccount("Worker", "Worker", 1000, OxydEconomyConstants.EmployerService, OxydEconomyConstants.WageLabour);

            var service = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerService)
                          ?? eco.CreateAccount("Service Department", "Service", 5000, OxydEconomyConstants.EmployerCargo, OxydEconomyConstants.BudgetBase, true, OxydEconomyConstants.EmployerService);
            service.Balance = 5000;

            var beforeMgr = manager.Balance;
            var beforeWorker = worker.Balance;
            eco.ForcePayday();

            Assert.That(manager.Balance, Is.EqualTo(beforeMgr));
            Assert.That(worker.Balance, Is.GreaterThan(beforeWorker));
        });
    }
}
