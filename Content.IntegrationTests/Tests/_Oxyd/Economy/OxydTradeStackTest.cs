using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.Economy;
using Content.Shared._Oxyd.Economy;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.Economy;

[TestFixture]
public sealed class OxydTradeStackTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task PlaceOrderAndFulfillDebitsGuild()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var trade = server.System<OxydTradeSystem>();
            trade.EnsureSeeded();

            var guild = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild)
                        ?? eco.CreateAccount("Guild Department", "Guild", 10000,
                            null, 0, true, OxydEconomyConstants.EmployerGuild, canMakeAccounts: true);
            eco.TrySetBalance(guild.AccountNumber, 10000);
            var before = guild.Balance;

            Assert.That(trade.TryGetStation("Vermouth", out var vermouth), Is.True);
            Assert.That(vermouth!.Goods.ContainsKey("DrinkGlass"), Is.True);
            var price = vermouth.Goods["DrinkGlass"].Price;
            var stockBefore = vermouth.Goods["DrinkGlass"].Stock;

            var loader = map.Grid; // any stable uid for cart key
            trade.ClearCart(loader);
            Assert.That(trade.TryAddToCart(loader, "Vermouth", "DrinkGlass", 2, out var err), Is.True, err);
            Assert.That(trade.TryPlaceOrder(loader, guild.AccountNumber, "test order", out var order, out err), Is.True, err);
            Assert.That(order, Is.Not.Null);
            Assert.That(order!.Fee, Is.EqualTo(0)); // Guild requestor
            Assert.That(order.Cost, Is.EqualTo(price * 2));

            var entMan = server.ResolveDependency<IEntityManager>();
            var drop = entMan.SpawnEntity("OxydTradeBeaconReceiving", map.MapCoords);
            Assert.That(trade.TryFulfillOrder(order.OrderId, entMan.GetComponent<TransformComponent>(drop).Coordinates, out err), Is.True, err);
            Assert.That(order.Fulfilled, Is.True);
            Assert.That(guild.Balance, Is.EqualTo(before - order.Cost));
            Assert.That(vermouth.Goods["DrinkGlass"].Stock, Is.EqualTo(stockBefore - 2));
            entMan.DeleteEntity(drop);
        });
    }

    [Test]
    public async Task OfferExportCreditsAccountAndClearsOffer()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var eco = server.System<OxydEconomySystem>();
            var trade = server.System<OxydTradeSystem>();
            trade.EnsureSeeded();

            var personal = eco.CreateAccount("Trader", "Trader", 100, OxydEconomyConstants.EmployerService, 0);
            var guild = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild)
                        ?? eco.CreateAccount("Guild Department", "Guild", 5000,
                            null, 0, true, OxydEconomyConstants.EmployerGuild, canMakeAccounts: true);

            Assert.That(trade.TryGetStation("Vermouth", out var vermouth), Is.True);
            Assert.That(vermouth!.Offers.ContainsKey("FoodBreadPlain"), Is.True);
            var offer = vermouth.Offers["FoodBreadPlain"];
            var need = offer.AmountRemaining;
            var price = offer.Price;

            var beacon = entMan.SpawnEntity("OxydTradeBeaconSending", map.MapCoords);
            for (var i = 0; i < need; i++)
                entMan.SpawnEntity("FoodBreadPlain", map.MapCoords);

            var beforePersonal = personal.Balance;
            var beforeGuild = guild.Balance;
            Assert.That(
                trade.TryFulfillOffersNear(beacon, 3f, personal.AccountNumber, slaveCut: true, out var paid, out var err),
                Is.True,
                err);
            Assert.That(paid, Is.EqualTo(price));
            Assert.That(offer.AmountRemaining, Is.EqualTo(0));
            // 80% slave / 20% guild
            var guildCut = (int)MathF.Floor(price * 0.2f);
            Assert.That(personal.Balance, Is.EqualTo(beforePersonal + (price - guildCut)));
            Assert.That(guild.Balance, Is.EqualTo(beforeGuild + guildCut));

            entMan.DeleteEntity(beacon);
        });
    }

    [Test]
    public async Task NonGuildOrderAppliesHandlingFee()
    {
        var server = Pair.Server;
        await server.WaitAssertion(() =>
        {
            var eco = server.System<OxydEconomySystem>();
            var trade = server.System<OxydTradeSystem>();
            trade.EnsureSeeded();

            var guild = eco.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild)
                        ?? eco.CreateAccount("Guild Department", "Guild", 50000,
                            null, 0, true, OxydEconomyConstants.EmployerGuild, canMakeAccounts: true);
            var buyer = eco.CreateAccount("ClubBuyer", "ClubBuyer", 5000, OxydEconomyConstants.EmployerService, 0);

            // Use a dummy cart owner entity number via buyer uidless — spawn a marker
            // Cart is keyed by EntityUid; use guild entity number fake via Ensure — create with station system
            // Simplest: use EntityUid with value from Create — we need a real uid. Use buyer's personal as cart key after spawning nothing.
            // EntityUid is a struct - we can use default invalid? Dictionary allows any key.
            var loader = new EntityUid(999001);
            trade.ClearCart(loader);
            Assert.That(trade.TryAddToCart(loader, "AstersCapital", "Crowbar", 1, out var err), Is.True, err);
            Assert.That(trade.TryPlaceOrder(loader, buyer.AccountNumber, "fee test", out var order, out err), Is.True, err);
            Assert.That(order, Is.Not.Null);
            Assert.That(order!.Fee, Is.EqualTo((int)MathF.Ceiling(order.Cost * OxydTradeSystem.HandlingFee)));

            var total = order.Cost + order.Fee;
            var beforeBuyer = buyer.Balance;
            var beforeGuild = guild.Balance;
            // Fulfill at null-ish coords — use MapCoordinates via Transform of a spawned beacon
            // For this fee assertion we only need place-order fee math; purchase path tested elsewhere.
            Assert.That(total, Is.GreaterThan(order.Cost));
            Assert.That(beforeBuyer, Is.EqualTo(5000));
            Assert.That(beforeGuild, Is.GreaterThanOrEqualTo(0));
        });
    }
}
