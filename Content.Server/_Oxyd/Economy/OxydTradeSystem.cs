using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Eris SStrade slice — discovered stations, cart, order queue, offers, export logs.
/// Buys debit Guild (or requester+fee); offer exports credit linked account (+ Guild cut if slave).
/// </summary>
public sealed partial class OxydTradeSystem : EntitySystem
{
    public const float HandlingFee = 0.1f;

    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly Dictionary<string, OxydTradeStationRuntime> _stations = new();
    private readonly List<OxydTradeOrderRuntime> _orders = new();
    private readonly List<OxydTradeLogRuntime> _logs = new();
    private int _nextOrderId = 1;
    private int _nextLogId = 1;
    private bool _seeded;
    private TimeSpan _nextRestock;

    /// <summary>Per-loader shopping carts keyed by loader EntityUid.</summary>


    public IReadOnlyDictionary<string, OxydTradeStationRuntime> Stations => _stations;
    public IReadOnlyList<OxydTradeOrderRuntime> Orders => _orders;
    public IReadOnlyList<OxydTradeLogRuntime> Logs => _logs;

    public override void Initialize()
    {
        base.Initialize();
        _nextRestock = _timing.CurTime + TimeSpan.FromMinutes(6);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _stations.Clear();
        _orders.Clear();
        _logs.Clear();
        _nextOrderId = 1;
        _nextLogId = 1;
        _seeded = false;
        _nextRestock = _timing.CurTime + TimeSpan.FromMinutes(6);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        EnsureSeeded();
        if (_timing.CurTime < _nextRestock)
            return;

        _nextRestock = _timing.CurTime + TimeSpan.FromMinutes(6);
        RestockTick();
    }

    public void EnsureSeeded()
    {
        if (_seeded)
            return;

        foreach (var proto in _protos.EnumeratePrototypes<OxydTradeStationPrototype>())
        {
            if (!_stations.ContainsKey(proto.ID))
                _stations[proto.ID] = CreateRuntime(proto);
        }

        _seeded = true;
    }

    private OxydTradeStationRuntime CreateRuntime(OxydTradeStationPrototype proto)
    {
        var rt = new OxydTradeStationRuntime
        {
            Id = proto.ID,
            Name = proto.Name,
            Description = proto.Description,
            Discovered = proto.StartDiscovered,
            Markup = proto.Markup,
            BaseIncome = proto.BaseIncome,
            Wealth = 0,
            Favor = 0,
        };

        foreach (var good in proto.Inventory)
        {
            var price = (int)MathF.Ceiling(good.Price * proto.Markup);
            rt.Goods[good.Product.Id] = new OxydTradeGoodRuntime
            {
                Product = good.Product.Id,
                Name = string.IsNullOrWhiteSpace(good.Name) ? good.Product.Id : good.Name,
                Category = good.Category,
                Price = price,
                Stock = good.Stock,
            };
        }

        foreach (var offer in proto.Offers)
        {
            rt.Offers[offer.Product.Id] = new OxydTradeOfferRuntime
            {
                Product = offer.Product.Id,
                Name = string.IsNullOrWhiteSpace(offer.Name) ? offer.Product.Id : offer.Name,
                Price = offer.Price,
                AmountRemaining = offer.Amount,
                AmountOriginal = offer.Amount,
            };
        }

        return rt;
    }

    public bool TryGetStation(string id, [NotNullWhen(true)] out OxydTradeStationRuntime? station)
        => _stations.TryGetValue(id, out station) && station.Discovered;

    /// <summary>Cart lives on the loader (PDA) so entity deletion cleans it up.</summary>
    public List<OxydTradeCartLineRuntime> GetCart(EntityUid loader)
        => EnsureComp<OxydTradeCartComponent>(loader).Lines;

    public void ClearCart(EntityUid loader) => GetCart(loader).Clear();

    public bool TryAddToCart(EntityUid loader, string stationId, string product, int amount, [NotNullWhen(false)] out string? error)
    {
        error = null;
        EnsureSeeded();
        if (amount <= 0)
        {
            error = "Invalid amount";
            return false;
        }

        if (!TryGetStation(stationId, out var station) || !station.Goods.TryGetValue(product, out var good))
        {
            error = "Unknown good";
            return false;
        }

        if (good.Stock < amount)
        {
            error = "Insufficient stock";
            return false;
        }

        var cart = GetCart(loader);
        var existing = cart.FirstOrDefault(c => c.StationId == stationId && c.Product == product);
        if (existing != null)
        {
            if (existing.Amount + amount > good.Stock)
            {
                error = "Insufficient stock";
                return false;
            }

            existing.Amount += amount;
        }
        else
        {
            cart.Add(new OxydTradeCartLineRuntime
            {
                StationId = stationId,
                Product = product,
                Name = good.Name,
                Price = good.Price,
                Amount = amount,
            });
        }

        return true;
    }

    public bool TryRemoveFromCart(EntityUid loader, string stationId, string product, int amount)
    {
        var cart = GetCart(loader);
        var existing = cart.FirstOrDefault(c => c.StationId == stationId && c.Product == product);
        if (existing == null)
            return false;

        existing.Amount -= Math.Max(1, amount);
        if (existing.Amount <= 0)
            cart.Remove(existing);
        return true;
    }

    public int CartTotal(EntityUid loader) => GetCart(loader).Sum(c => c.Price * c.Amount);

    public bool TryPlaceOrder(EntityUid loader, int requestingAccount, string reason, [NotNullWhen(true)] out OxydTradeOrderRuntime? order, [NotNullWhen(false)] out string? error)
    {
        order = null;
        error = null;
        EnsureSeeded();
        _economy.EnsureDepartmentsForRound();

        var cart = GetCart(loader);
        if (cart.Count == 0)
        {
            error = "Cart empty";
            return false;
        }

        if (!_economy.TryGetAccount(requestingAccount, out var acct) || acct.Suspended)
        {
            error = "Account unavailable";
            return false;
        }

        // Validate stock snapshot
        foreach (var line in cart)
        {
            if (!TryGetStation(line.StationId, out var st) || !st.Goods.TryGetValue(line.Product, out var good) || good.Stock < line.Amount)
            {
                error = $"Out of stock: {line.Name}";
                return false;
            }
        }

        var cost = cart.Sum(c => c.Price * c.Amount);
        var isGuild = acct.IsDepartment && acct.DepartmentId == OxydEconomyConstants.EmployerGuild;
        var fee = isGuild ? 0 : (int)MathF.Ceiling(cost * HandlingFee);

        order = new OxydTradeOrderRuntime
        {
            OrderId = $"order_{_nextOrderId++}",
            Reason = string.IsNullOrWhiteSpace(reason) ? "Trade order" : reason.Trim(),
            Cost = cost,
            Fee = fee,
            RequestingAccount = requestingAccount,
            RequestingName = acct.OwnerName,
            Lines = cart.Select(c => c.Clone()).ToList(),
            CreatedAt = _timing.CurTime,
        };
        _orders.Add(order);
        ClearCart(loader);
        LogEntry("Order", acct.OwnerName, order.ContentsSummary, cost + fee);
        return true;
    }

    public bool TryCancelOrder(string orderId, int? actorAccount, [NotNullWhen(false)] out string? error)
    {
        error = null;
        var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
        if (order == null)
        {
            error = "Unknown order";
            return false;
        }

        if (actorAccount != null && order.RequestingAccount != actorAccount.Value)
        {
            error = "Not your order";
            return false;
        }

        if (order.Fulfilled)
        {
            error = "Already fulfilled";
            return false;
        }

        _orders.Remove(order);
        return true;
    }

    /// <summary>Purchase an order at a receiving beacon — debit requester (or Guild), spawn goods.
    /// fulfillerAccounts: the fulfiller's personal/department accounts — one must be the
    /// requester or the Guild, otherwise anyone could deliver someone else's order.</summary>
    public bool TryFulfillOrder(string orderId, EntityCoordinates dropAt, int[]? fulfillerAccounts, [NotNullWhen(false)] out string? error)
    {
        error = null;
        EnsureSeeded();
        _economy.EnsureDepartmentsForRound();

        var order = _orders.FirstOrDefault(o => o.OrderId == orderId);
        if (order == null || order.Fulfilled)
        {
            error = "Order unavailable";
            return false;
        }

        if (!_economy.TryGetAccount(order.RequestingAccount, out var requester) || requester.Suspended)
        {
            error = "Requester account unavailable";
            return false;
        }

        var guild = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild);
        if (guild == null)
        {
            error = "Guild account missing";
            return false;
        }

        // Ownership: only the requester themselves or the Guild can pick the order up.
        if (fulfillerAccounts == null ||
            !fulfillerAccounts.Any(a => a == order.RequestingAccount || a == guild.AccountNumber))
        {
            error = "Not authorized to fulfill this order";
            return false;
        }

        var total = order.Cost + order.Fee;
        var isGuild = requester.IsDepartment && requester.DepartmentId == OxydEconomyConstants.EmployerGuild;

        // Reserve stock
        foreach (var line in order.Lines)
        {
            if (!TryGetStation(line.StationId, out var st) || !st.Goods.TryGetValue(line.Product, out var good) || good.Stock < line.Amount)
            {
                error = $"Out of stock: {line.Name}";
                return false;
            }
        }

        if (isGuild)
        {
            if (!_economy.TryAdjust(guild.AccountNumber, -order.Cost, $"Trade buy {order.OrderId}", "TradeBeacon"))
            {
                error = "Guild funds insufficient";
                return false;
            }
        }
        else
        {
            // Fee is the Guild's cut; the goods cost goes to the station (Cargo)
            // rather than also being pocketed by the Guild.
            var costDest = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerCargo) ?? guild;
            if (!_economy.TryGetAccount(order.RequestingAccount, out var reqAcc) ||
                reqAcc.Balance < order.Fee + order.Cost)
            {
                error = "Insufficient funds";
                return false;
            }

            if (order.Fee > 0 &&
                !_economy.TryTransfer(order.RequestingAccount, guild.AccountNumber, order.Fee, $"Trade order fee {order.OrderId}", "TradeBeacon", out error))
            {
                error ??= "Payment failed";
                return false;
            }

            if (!_economy.TryTransfer(order.RequestingAccount, costDest.AccountNumber, order.Cost, $"Trade order {order.OrderId}", "TradeBeacon", out error))
            {
                // Refund the fee so a half-failed payment doesn't charge it per retry.
                _economy.TryTransfer(guild.AccountNumber, order.RequestingAccount, order.Fee, $"Refund trade fee {order.OrderId}", "TradeBeacon", out _);
                error ??= "Payment failed";
                return false;
            }
        }

        foreach (var line in order.Lines)
        {
            var st = _stations[line.StationId];
            var good = st.Goods[line.Product];
            good.Stock -= line.Amount;
            st.Wealth += line.Price * line.Amount;
            st.Favor += line.Price * line.Amount;

            for (var i = 0; i < line.Amount; i++)
                Spawn(line.Product, dropAt);
        }

        order.Fulfilled = true;
        LogEntry("Shipping", requester.OwnerName, order.ContentsSummary, total);
        return true;
    }

    /// <summary>Fulfill all matching offers using entities near a sending beacon.</summary>
    public bool TryFulfillOffersNear(
        EntityUid beacon,
        float range,
        int payoutAccount,
        bool slaveCut,
        out int paidTotal,
        [NotNullWhen(false)] out string? error)
    {
        paidTotal = 0;
        error = null;
        EnsureSeeded();
        _economy.EnsureDepartmentsForRound();

        if (!_economy.TryGetAccount(payoutAccount, out var payout) || payout.Suspended)
        {
            error = "Payout account unavailable";
            return false;
        }

        var xform = Transform(beacon);
        var nearby = _lookup.GetEntitiesInRange(xform.Coordinates, range);
        // Group by prototype id
        var byProto = new Dictionary<string, List<EntityUid>>();
        foreach (var ent in nearby)
        {
            if (ent == beacon)
                continue;
            var proto = MetaData(ent).EntityPrototype?.ID;
            if (proto == null)
                continue;
            if (!byProto.TryGetValue(proto, out var list))
            {
                list = new List<EntityUid>();
                byProto[proto] = list;
            }

            list.Add(ent);
        }

        var any = false;
        var invoice = new List<string>();

        foreach (var station in _stations.Values.Where(s => s.Discovered).Reverse())
        {
            foreach (var (product, offer) in station.Offers.ToList())
            {
                if (offer.AmountRemaining <= 0)
                    continue;
                if (!byProto.TryGetValue(product, out var items) || items.Count < offer.AmountRemaining)
                    continue;

                var take = offer.AmountRemaining;
                for (var i = 0; i < take; i++)
                {
                    var uid = items[i];
                    invoice.Add(MetaData(uid).EntityName);
                    QueueDel(uid);
                }

                items.RemoveRange(0, take);
                var price = offer.Price;
                offer.AmountRemaining = 0;
                station.Wealth += price;
                station.Favor += price;
                paidTotal += price;
                any = true;

                if (slaveCut)
                {
                    var guild = _economy.FindDepartmentAccount(OxydEconomyConstants.EmployerGuild);
                    var guildCut = (int)MathF.Floor(price * 0.2f);
                    var slaveCutAmt = price - guildCut;
                    if (guild != null && guild.AccountNumber != payoutAccount)
                    {
                        _economy.TrySilentAdjust(guild.AccountNumber, guildCut, "Trade offer Guild cut", station.Name);
                        _economy.TrySilentAdjust(payoutAccount, slaveCutAmt, "Trade offer", station.Name);
                    }
                    else
                    {
                        _economy.TrySilentAdjust(payoutAccount, price, "Trade offer", station.Name);
                    }
                }
                else
                {
                    _economy.TrySilentAdjust(payoutAccount, price, "Trade offer", station.Name);
                }
            }
        }

        if (!any)
        {
            error = "No matching offers";
            return false;
        }

        LogEntry("Special Offer", payout.OwnerName, string.Join(", ", invoice), paidTotal);
        return true;
    }

    public void DiscoverStation(string id)
    {
        EnsureSeeded();
        if (_stations.TryGetValue(id, out var st))
            st.Discovered = true;
    }

    public void RestockTick()
    {
        EnsureSeeded();
        foreach (var st in _stations.Values)
        {
            if (!st.Discovered)
                continue;

            st.Wealth += st.BaseIncome;
            var unique = Math.Max(1, st.Goods.Count);
            var budget = st.Wealth / unique;
            foreach (var good in st.Goods.Values)
            {
                if (good.Stock >= 20)
                    continue;
                var chance = good.Stock < 5 ? 100 : 15;
                if (System.Random.Shared.Next(1, 101) > chance)
                    continue;
                var unitCost = Math.Max(1, good.Price / 2);
                var add = Math.Clamp(budget / unitCost, 1, 5);
                var cost = add * unitCost;
                if (cost > st.Wealth)
                    continue;
                st.Wealth -= cost;
                good.Stock += add;
            }

            // Refresh exhausted offers from prototype
            if (_protos.TryIndex<OxydTradeStationPrototype>(st.Id, out var proto))
            {
                foreach (var offer in proto.Offers)
                {
                    if (!st.Offers.TryGetValue(offer.Product.Id, out var rt) || rt.AmountRemaining > 0)
                        continue;
                    rt.AmountRemaining = offer.Amount;
                    rt.Price = offer.Price;
                }
            }
        }
    }

    private void LogEntry(string type, string account, string contents, int paid)
    {
        var prefix = type switch
        {
            "Shipping" => "S",
            "Export" => "E",
            "Special Offer" => "SO",
            "Order" => "O",
            _ => "X",
        };
        _logs.Add(new OxydTradeLogRuntime
        {
            Id = $"{_nextLogId++}-{prefix}",
            Type = type,
            Account = account,
            Contents = contents,
            Paid = paid,
            Time = _timing.CurTime,
        });
        if (_logs.Count > 64)
            _logs.RemoveRange(0, _logs.Count - 64);
    }
}

public sealed class OxydTradeStationRuntime
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public bool Discovered;
    public float Markup = 1.2f;
    public int BaseIncome;
    public int Wealth;
    public int Favor;
    public Dictionary<string, OxydTradeGoodRuntime> Goods = new();
    public Dictionary<string, OxydTradeOfferRuntime> Offers = new();
}

public sealed class OxydTradeGoodRuntime
{
    public string Product = string.Empty;
    public string Name = string.Empty;
    public string Category = string.Empty;
    public int Price;
    public int Stock;
}

public sealed class OxydTradeOfferRuntime
{
    public string Product = string.Empty;
    public string Name = string.Empty;
    public int Price;
    public int AmountRemaining;
    public int AmountOriginal;
}

public sealed class OxydTradeCartLineRuntime
{
    public string StationId = string.Empty;
    public string Product = string.Empty;
    public string Name = string.Empty;
    public int Price;
    public int Amount;

    public OxydTradeCartLineRuntime Clone() => new()
    {
        StationId = StationId,
        Product = Product,
        Name = Name,
        Price = Price,
        Amount = Amount,
    };
}

public sealed class OxydTradeOrderRuntime
{
    public string OrderId = string.Empty;
    public string Reason = string.Empty;
    public int Cost;
    public int Fee;
    public int RequestingAccount;
    public string RequestingName = string.Empty;
    public bool Fulfilled;
    public List<OxydTradeCartLineRuntime> Lines = new();
    public TimeSpan CreatedAt;

    public string ContentsSummary =>
        string.Join(", ", Lines.Select(l => $"{l.Amount}x {l.Name}"));
}

public sealed class OxydTradeLogRuntime
{
    public string Id = string.Empty;
    public string Type = string.Empty;
    public string Account = string.Empty;
    public string Contents = string.Empty;
    public int Paid;
    public TimeSpan Time;
}
