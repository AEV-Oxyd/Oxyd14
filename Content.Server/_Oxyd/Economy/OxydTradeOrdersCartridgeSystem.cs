using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris trade_orders PDA program — browse stations, cart, orders, offers.</summary>
public sealed partial class OxydTradeOrdersCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private OxydTradeSystem _trade = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;



    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydTradeOrdersCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydTradeOrdersCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydTradeOrdersCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        PushUi(uid, args.Loader);
    }

    private void OnMessage(EntityUid uid, OxydTradeOrdersCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydTradeOrdersUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;
        string? status = null;

        switch (msg.Action)
        {
            case OxydTradeUiAction.Login:
                if (_economy.TryLogin(msg.AccountNumber, msg.Pin, out _, "TradeOrders", loader))
                    EnsureComp<OxydTradeOrdersSessionComponent>(loader).AccountNumber = msg.AccountNumber;
                else
                    error = "Login failed";
                break;
            case OxydTradeUiAction.Logout:
                // Reset the session fields rather than defer-remove: the UI push
                // below must already see the logged-out state.
                if (TryComp<OxydTradeOrdersSessionComponent>(loader, out var logSess))
                {
                    logSess.AccountNumber = 0;
                    logSess.SelectedStation = null;
                    logSess.Tab = OxydTradeUiTab.Stations;
                }
                break;
            case OxydTradeUiAction.SetTab:
                EnsureComp<OxydTradeOrdersSessionComponent>(loader).Tab = msg.Tab;
                break;
            case OxydTradeUiAction.SelectStation:
                {
                    var s = EnsureComp<OxydTradeOrdersSessionComponent>(loader);
                    s.SelectedStation = msg.StationId;
                    s.Tab = OxydTradeUiTab.Goods;
                }
                break;
            case OxydTradeUiAction.AddToCart:
                if (!RequireSession(loader, out _))
                {
                    error = "Not logged in";
                    break;
                }

                if (!_trade.TryAddToCart(loader, msg.StationId ?? string.Empty, msg.Product ?? string.Empty, Math.Max(1, msg.Amount), out error))
                    error ??= "Add failed";
                else
                    status = "Added to cart";
                break;
            case OxydTradeUiAction.RemoveFromCart:
                _trade.TryRemoveFromCart(loader, msg.StationId ?? string.Empty, msg.Product ?? string.Empty, Math.Max(1, msg.Amount));
                break;
            case OxydTradeUiAction.ClearCart:
                _trade.ClearCart(loader);
                status = "Cart cleared";
                break;
            case OxydTradeUiAction.PlaceOrder:
                if (!RequireSession(loader, out var acct))
                {
                    error = "Not logged in";
                    break;
                }

                if (_trade.TryPlaceOrder(loader, acct, msg.Reason ?? "Trade order", out var order, out error) && order != null)
                    status = $"Placed {order.OrderId}";
                else
                    error ??= "Order failed";
                break;
            case OxydTradeUiAction.CancelOrder:
                if (!RequireSession(loader, out var cancelAcct))
                {
                    error = "Not logged in";
                    break;
                }

                if (!_trade.TryCancelOrder(msg.OrderId ?? string.Empty, cancelAcct, out error))
                    error ??= "Cancel failed";
                else
                    status = "Order cancelled";
                break;
            case OxydTradeUiAction.FulfillOrder:
                if (!RequireSession(loader, out var fulfillAcct))
                {
                    error = "Not logged in";
                    break;
                }

                if (!TryFindReceivingBeacon(loader, out var recv, out var dropAt))
                {
                    error = "No receiving beacon in range";
                    break;
                }

                if (_trade.TryFulfillOrder(msg.OrderId ?? string.Empty, dropAt, new[] { fulfillAcct }, out error))
                    status = $"Fulfilled at {ToPrettyString(recv)}";
                else
                    error ??= "Fulfill failed";
                break;
            case OxydTradeUiAction.FulfillOffers:
                if (!RequireSession(loader, out var payAcct))
                {
                    error = "Not logged in";
                    break;
                }

                if (!TryFindSendingBeacon(loader, out var send, out var sendComp))
                {
                    error = "No sending beacon in range";
                    break;
                }

                var slave = _economy.TryGetAccount(payAcct, out var pa)
                            && !(pa.IsDepartment && pa.DepartmentId == OxydEconomyConstants.EmployerGuild);
                if (_trade.TryFulfillOffersNear(send, sendComp.Range, payAcct, slave, out var paid, out error))
                    status = $"Exported offers for {paid}cr";
                else
                    error ??= "Export failed";
                break;
            case OxydTradeUiAction.Refresh:
                break;
        }

        PushUi(uid, loader, error, status);
    }

    private bool RequireSession(EntityUid loader, out int account)
    {
        account = 0;
        if (!TryComp<OxydTradeOrdersSessionComponent>(loader, out var s) || s.AccountNumber == 0)
            return false;
        account = s.AccountNumber;
        return true;
    }

    private bool TryFindReceivingBeacon(EntityUid loader, out EntityUid beacon, out EntityCoordinates dropAt)
    {
        beacon = default;
        dropAt = default;
        var xform = Transform(loader);
        foreach (var ent in _lookup.GetEntitiesInRange(xform.Coordinates, 4f))
        {
            if (!HasComp<OxydTradeBeaconReceivingComponent>(ent))
                continue;
            beacon = ent;
            dropAt = Transform(ent).Coordinates;
            return true;
        }

        return false;
    }

    private bool TryFindSendingBeacon(EntityUid loader, out EntityUid beacon, out OxydTradeBeaconSendingComponent comp)
    {
        beacon = default;
        comp = default!;
        var xform = Transform(loader);
        foreach (var ent in _lookup.GetEntitiesInRange(xform.Coordinates, 4f))
        {
            if (!TryComp(ent, out OxydTradeBeaconSendingComponent? c))
                continue;
            beacon = ent;
            comp = c;
            return true;
        }

        return false;
    }

    private void PushUi(EntityUid cartUid, EntityUid loader, string? error = null, string? status = null)
    {
        _trade.EnsureSeeded();
        _economy.EnsureDepartmentsForRound();

        var tab = OxydTradeUiTab.Stations;
        string? selected = null;
        if (TryComp<OxydTradeOrdersSessionComponent>(loader, out var sess))
        {
            tab = sess.Tab;
            selected = sess.SelectedStation;
        }
        var state = new OxydTradeOrdersUiState
        {
            Tab = tab,
            SelectedStationId = selected,
            Error = error,
            Status = status,
        };

        if (RequireSession(loader, out var acct) && _economy.TryGetAccount(acct, out var data))
        {
            state.LoggedIn = true;
            state.AccountNumber = data.AccountNumber;
            state.OwnerName = data.OwnerName;
            state.Balance = data.Balance;
            state.IsGuild = data.IsDepartment && data.DepartmentId == OxydEconomyConstants.EmployerGuild;
        }

        state.Stations = _trade.Stations.Values
            .Where(s => s.Discovered)
            .Select(s => new OxydTradeStationSummary
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Wealth = s.Wealth,
                Favor = s.Favor,
                UniqueGoods = s.Goods.Count,
                ActiveOffers = s.Offers.Values.Count(o => o.AmountRemaining > 0),
            })
            .OrderBy(s => s.Name)
            .ToList();

        if (selected != null && _trade.TryGetStation(selected, out var st))
        {
            state.Goods = st.Goods.Values
                .Select(g => new OxydTradeGoodView
                {
                    StationId = st.Id,
                    Product = g.Product,
                    Name = g.Name,
                    Category = g.Category,
                    Price = g.Price,
                    Stock = g.Stock,
                })
                .OrderBy(g => g.Category)
                .ThenBy(g => g.Name)
                .ToList();
        }

        state.Cart = _trade.GetCart(loader)
            .Select(c => new OxydTradeCartLine
            {
                StationId = c.StationId,
                Product = c.Product,
                Name = c.Name,
                Price = c.Price,
                Amount = c.Amount,
            })
            .ToList();
        state.CartTotal = _trade.CartTotal(loader);

        state.Orders = _trade.Orders
            .OrderByDescending(o => o.CreatedAt)
            .Take(24)
            .Select(o => new OxydTradeOrderView
            {
                OrderId = o.OrderId,
                Reason = o.Reason,
                Cost = o.Cost,
                Fee = o.Fee,
                RequestingAccount = o.RequestingAccount,
                RequestingName = o.RequestingName,
                Fulfilled = o.Fulfilled,
                ContentsSummary = o.ContentsSummary,
            })
            .ToList();

        state.Offers = _trade.Stations.Values
            .Where(s => s.Discovered)
            .SelectMany(s => s.Offers.Values
                .Where(o => o.AmountRemaining > 0)
                .Select(o => new OxydTradeOfferView
                {
                    StationId = s.Id,
                    StationName = s.Name,
                    Product = o.Product,
                    Name = o.Name,
                    Price = o.Price,
                    AmountRemaining = o.AmountRemaining,
                }))
            .OrderBy(o => o.StationName)
            .ThenBy(o => o.Name)
            .ToList();

        state.Logs = _trade.Logs
            .AsEnumerable()
            .Reverse()
            .Take(24)
            .Select(l => new OxydTradeLogView
            {
                Id = l.Id,
                Type = l.Type,
                Account = l.Account,
                Contents = l.Contents,
                Paid = l.Paid,
                Time = l.Time.ToString(@"hh\:mm\:ss"),
            })
            .ToList();

        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
