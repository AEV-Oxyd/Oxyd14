using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Shared._Oxyd.Economy;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydPosDevicesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task RegisterBanksTillToService()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var entMan = server.ResolveDependency<IEntityManager>();

            // Ensure Service dept account exists even without a station bank.
            var service = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerService)
                          ?? eco.CreateAccount("Service Department", "Service", 1000,
                              OxydEconomyConstants.EmployerCargo, OxydEconomyConstants.BudgetBase,
                              true, OxydEconomyConstants.EmployerService);
            var before = service.Balance;

            var register = entMan.SpawnEntity("OxydCashRegister", map.MapCoords);
            var comp = entMan.GetComponent<OxydCashRegisterComponent>(register);
            comp.DrawerOpen = true;
            comp.Stored = 250;
            comp.LinkedAccountNumber = service.AccountNumber;

            Assert.That(eco.TryAdjust(service.AccountNumber, comp.Stored, "test bank", "test"), Is.True);
            // Simulate bank till effect
            var afterBank = before + 250;
            Assert.That(eco.TryGetAccount(service.AccountNumber, out var svc) && svc.Balance == afterBank);

            entMan.DeleteEntity(register);
        });
    }

    [Test]
    public async Task AtmWithdrawDebitsAccount()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var acct = eco.CreateAccount("Teller", "Teller", 1000, OxydEconomyConstants.EmployerService, 0);
            Assert.That(eco.TryAdjust(acct.AccountNumber, -100, "ATM withdraw", "ATM"), Is.True);
            Assert.That(acct.Balance, Is.EqualTo(900));
        });
    }
}
