using System.Linq;
using Content.Server._Oxyd.Economy;
using Content.Shared._Oxyd.AstersGuild;
using Content.Shared._Oxyd.Economy;
using Content.Shared.GameTicking;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using EconomyBeaconRecv = Content.Shared._Oxyd.Economy.OxydTradeBeaconReceivingComponent;
using EconomyBeaconSend = Content.Shared._Oxyd.Economy.OxydTradeBeaconSendingComponent;
using GoodEntry = Content.Shared._Oxyd.AstersGuild.OxydTradeGoodEntry;
using OfferEntry = Content.Shared._Oxyd.AstersGuild.OxydTradeOfferEntry;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Server backend for the Asters trade console — ports the Eris trade program
/// (code/controllers/subsystems/trade.dm). Stations, goods and offers are the
/// <see cref="OxydTradeStationPrototype"/> YAML, run by <see cref="OxydTradeSystem"/>; this
/// system only drives the console window. The console spends from and is paid into the
/// account it is logged into, which starts as its department's account.
/// </summary>
public sealed partial class OxydGuildTradeConsoleSystem : EntitySystem
{
    private static readonly EntProtoId TradeCrate = "OxydTradeCrate";

    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private OxydTradeSystem _trade = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>One crew order request on the merchant queue (Eris trade_orders).
    /// Typed in by players at runtime, so there is nothing to put in YAML.</summary>
    private sealed class Request
    {
        public int Id;
        public string Item = string.Empty;
        public int Amount;
        public string From = string.Empty;
    }

    private readonly List<Request> _requests = new();
    private int _nextRequestId;
    private float _updateTimer;

    [SubscribeLocalEvent]
    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _requests.Clear();
        _nextRequestId = 0;
    }

    [SubscribeLocalEvent]
    private void OnOpened(Entity<OxydTradeConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTimer -= frameTime;
        if (_updateTimer > 0)
            return;

        _updateTimer = 1f;
        var query = EntityQueryEnumerator<OxydTradeConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
            PushState((uid, comp));
    }

    /// <summary>Stations in a fixed order so UI indices stay valid between states.</summary>
    private List<OxydTradeStationRuntime> StationList()
    {
        _trade.EnsureSeeded();
        return _trade.Stations.Values.ToList();
    }

    private OxydTradeStationRuntime? SelectedStation(OxydTradeConsoleComponent comp)
    {
        var stations = StationList();
        return comp.SelectedStation >= 0 && comp.SelectedStation < stations.Count ? stations[comp.SelectedStation] : null;
    }

    /// <summary>Open offers, in the order the UI numbers them.</summary>
    private List<(OxydTradeStationRuntime Station, OxydTradeOfferRuntime Offer)> OpenOffers()
    {
        var offers = new List<(OxydTradeStationRuntime, OxydTradeOfferRuntime)>();
        foreach (var station in StationList())
        {
            if (!station.Discovered)
                continue;

            foreach (var offer in station.Offers.Values)
            {
                if (offer.AmountRemaining > 0 && offer.ExpiresAt > _timing.CurTime)
                    offers.Add((station, offer));
            }
        }

        return offers;
    }

    private OxydMoneyAccountData? ConsoleAccount(OxydTradeConsoleComponent comp)
    {
        if (comp.Account is { } number)
            return _economy.TryGetAccount(number, out var acc) ? acc : null;

        return _economy.FindDepartmentAccount(comp.DefaultDepartment);
    }

    private bool BeaconCheck(out string receiving, out string sending)
    {
        var recv = EntityQueryEnumerator<EconomyBeaconRecv>().MoveNext(out _, out _);
        var send = EntityQueryEnumerator<EconomyBeaconSend>().MoveNext(out _, out _);
        receiving = recv ? "✓Receiving Beacon" : "✗No Receiving Beacon";
        sending = send ? "✓Sending Beacon" : "✗No Sending Beacon";
        return recv && send;
    }

    private void PushState(Entity<OxydTradeConsoleComponent> ent)
    {
        var (uid, comp) = ent;
        if (!_ui.IsUiOpen(uid, OxydTradeUiKey.Key))
            return;

        BeaconCheck(out var recv, out var send);
        var account = ConsoleAccount(comp);
        var s = new OxydTradeState
        {
            Balance = account?.Balance ?? 0,
            HasAccount = account != null,
            AccountNumber = account?.AccountNumber,
            AccountName = account?.OwnerName ?? string.Empty,
            ReceivingBeacon = recv,
            SendingBeacon = send,
            SelectedStation = comp.SelectedStation,
            Category = comp.Category,
            IsRequestConsole = comp.RequestConsole,
        };

        foreach (var r in _requests)
        {
            s.Requests.Add(new OxydTradeRequestEntry
            {
                Id = r.Id, Item = r.Item, Amount = r.Amount, From = r.From,
            });
        }

        foreach (var c in comp.SavedCarts)
            s.SavedCartNames.Add(c.Name);

        var offers = OpenOffers();
        foreach (var st in StationList())
        {
            var stationOffers = offers.Where(o => o.Station == st).ToList();
            s.Stations.Add(new OxydTradeStationEntry
            {
                Name = st.Discovered ? st.Name : "???",
                Nick = st.Id,
                Description = st.Description,
                Discovered = st.Discovered,
                Favor = st.Favor,
                FavorMax = st.FavorMax,
                OfferSecondsLeft = stationOffers.Count == 0
                    ? 0
                    : (int)stationOffers.Max(o => (o.Offer.ExpiresAt - _timing.CurTime).TotalSeconds),
            });
        }

        if (SelectedStation(comp) is { } selected)
        {
            s.SelectedName = selected.Name;
            s.SelectedDesc = selected.Description;
            var goods = selected.Goods.Values.ToList();
            s.Categories.AddRange(goods.Select(g => g.Category).Distinct());
            for (var i = 0; i < goods.Count; i++)
            {
                var g = goods[i];
                if (comp.Category != null && g.Category != comp.Category)
                    continue;

                s.Goods.Add(new GoodEntry
                {
                    Id = i, Name = g.Name, Price = g.Price, Available = g.Stock,
                    InCart = comp.Cart.GetValueOrDefault(i),
                });
            }

            foreach (var (id, n) in comp.Cart)
            {
                if (id < 0 || id >= goods.Count)
                    continue;

                var g = goods[id];
                s.CartCount += n;
                s.CartTotal += n * g.Price;
                s.CartLines.Add($"{g.Name} x{n} — {n * g.Price}cr");
            }
        }

        for (var i = 0; i < offers.Count; i++)
        {
            var (station, offer) = offers[i];
            s.Offers.Add(new OfferEntry
            {
                Id = i, Station = station.Name, Name = offer.Name, Price = offer.Price,
                Amount = offer.AmountRemaining,
                SecondsLeft = (int)(offer.ExpiresAt - _timing.CurTime).TotalSeconds,
            });
        }

        s.Log.AddRange(comp.Log);
        _ui.SetUiState(uid, OxydTradeUiKey.Key, s);
    }

    [SubscribeLocalEvent]
    private void OnSetAccount(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSetAccountMessage msg)
    {
        if (!_economy.TryLogin(msg.AccountNumber, msg.Pin, out var acc, "TradeConsole", msg.Actor))
        {
            ent.Comp.Log.Add($"[Account] login to #{msg.AccountNumber} failed");
            PushState(ent);
            return;
        }

        ent.Comp.Account = acc.AccountNumber;
        ent.Comp.Log.Add($"[Account] now using #{acc.AccountNumber} {acc.OwnerName}");
        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnSelectStation(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSelectStationMessage msg)
    {
        var stations = StationList();
        if (msg.Index < 0 || msg.Index >= stations.Count || !stations[msg.Index].Discovered)
            return;

        ent.Comp.SelectedStation = msg.Index;
        ent.Comp.Category = null;
        ent.Comp.Cart.Clear();
        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnSelectCategory(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSelectCategoryMessage msg)
    {
        ent.Comp.Category = msg.Category;
        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnCartAdjust(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeCartAdjustMessage msg)
    {
        if (SelectedStation(ent.Comp) is not { } st)
            return;

        var goods = st.Goods.Values.ToList();
        if (msg.GoodId < 0 || msg.GoodId >= goods.Count)
            return;

        var cart = ent.Comp.Cart;
        var n = msg.Delta == 0 ? 0 : Math.Clamp(cart.GetValueOrDefault(msg.GoodId) + msg.Delta, 0, goods[msg.GoodId].Stock);
        if (n == 0)
            cart.Remove(msg.GoodId);
        else
            cart[msg.GoodId] = n;

        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnResetCart(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeResetCartMessage msg)
    {
        ent.Comp.Cart.Clear();
        PushState(ent);
    }

    /// <summary>Eris "Create Order": the console's account pays and the goods arrive in a
    /// crate on the receiving beacon.</summary>
    [SubscribeLocalEvent]
    private void OnCreateOrder(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeCreateOrderMessage msg)
    {
        var comp = ent.Comp;
        // Crew request consoles only file requests — they can't spend.
        if (comp.RequestConsole || comp.Cart.Count == 0 || SelectedStation(comp) is not { } st)
            return;

        if (ConsoleAccount(comp) is not { } account)
        {
            comp.Log.Add("[Order] no account");
            PushState(ent);
            return;
        }

        var goods = st.Goods.Values.ToList();
        var wants = new Dictionary<string, int>();
        foreach (var (id, n) in comp.Cart)
        {
            if (id >= 0 && id < goods.Count)
                wants[goods[id].Product] = n;
        }

        if (!_trade.TryPurchase(st.Id, wants, account.AccountNumber, "TradeConsole", out var total, out var taken, out var error))
        {
            comp.Log.Add($"[Order] {st.Name}: {error}");
            PushState(ent);
            return;
        }

        var dropAt = EntityQueryEnumerator<EconomyBeaconRecv>().MoveNext(out var beacon, out _) ? beacon : ent.Owner;
        var at = Transform(dropAt).Coordinates;

        // Eris delivers a shipping crate on the receiving beacon (order.dm).
        var crate = Spawn(TradeCrate, at);
        TryComp(crate, out EntityStorageComponent? storage);
        var delivered = new List<string>();
        foreach (var (product, take) in taken)
        {
            for (var i = 0; i < take; i++)
            {
                var item = Spawn(product, at);
                if (storage != null)
                    _container.Insert(item, storage.Contents);
            }

            delivered.Add($"{st.Goods[product].Name} x{take}");
        }

        comp.Log.Add($"[Order] {st.Name}: {string.Join(", ", delivered)} — {total}cr");
        comp.Cart.Clear();
        PushState(ent);
    }

    /// <summary>Eris offer send: the nearest sending beacon exports the matching goods and the
    /// console's account is paid.</summary>
    [SubscribeLocalEvent]
    private void OnSendOffer(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSendOfferMessage msg)
    {
        var comp = ent.Comp;
        if (comp.RequestConsole)
            return;

        var offers = OpenOffers();
        if (msg.OfferId < 0 || msg.OfferId >= offers.Count || ConsoleAccount(comp) is not { } account)
            return;

        var (station, offer) = offers[msg.OfferId];
        if (NearestSendingBeacon(ent.Owner) is not { } beacon)
            return;

        if (!_trade.TryFulfillOffersNear(beacon, 1.5f, account.AccountNumber, false, out var paid, out var error,
                (station.Id, offer.Product)))
        {
            comp.Log.Add($"[Offer] {station.Name}: {error}");
            PushState(ent);
            return;
        }

        comp.Log.Add($"[Offer] {station.Name}: {offer.Name} x{offer.AmountOriginal} — +{paid}cr");
        PushState(ent);
    }

    private EntityUid? NearestSendingBeacon(EntityUid console)
    {
        EntityUid? beacon = null;
        var bestDist = float.MaxValue;
        var myPos = _transform.GetWorldPosition(console);
        var query = EntityQueryEnumerator<EconomyBeaconSend, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var d = (_transform.GetWorldPosition(xform) - myPos).LengthSquared();
            if (d >= bestDist)
                continue;

            bestDist = d;
            beacon = uid;
        }

        return beacon;
    }

    /// <summary>Eris saved_carts: snapshot the cart under a name.</summary>
    [SubscribeLocalEvent]
    private void OnSaveCart(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSaveCartMessage msg)
    {
        var comp = ent.Comp;
        if (comp.Cart.Count == 0 || string.IsNullOrWhiteSpace(msg.Name) || SelectedStation(comp) is not { } st)
            return;

        var name = msg.Name.Trim();
        comp.SavedCarts.RemoveAll(c => c.Name == name);
        comp.SavedCarts.Add(new OxydSavedCart
        {
            Name = name,
            Station = st.Id,
            Items = new Dictionary<int, int>(comp.Cart),
        });
        PushState(ent);
    }

    /// <summary>Eris cart load: return to the cart's station and restore it.</summary>
    [SubscribeLocalEvent]
    private void OnLoadCart(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeLoadCartMessage msg)
    {
        var comp = ent.Comp;
        if (msg.Index < 0 || msg.Index >= comp.SavedCarts.Count)
            return;

        var saved = comp.SavedCarts[msg.Index];
        var index = StationList().FindIndex(s => s.Id == saved.Station);
        if (index < 0)
            return;

        comp.SelectedStation = index;
        comp.Category = null;
        comp.Cart.Clear();
        foreach (var (k, v) in saved.Items)
            comp.Cart[k] = v;
        PushState(ent);
    }

    [SubscribeLocalEvent]
    private void OnDeleteSavedCart(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeDeleteSavedCartMessage msg)
    {
        if (msg.Index < 0 || msg.Index >= ent.Comp.SavedCarts.Count)
            return;

        ent.Comp.SavedCarts.RemoveAt(msg.Index);
        PushState(ent);
    }

    /// <summary>Eris trade_orders: crew console pushes a request onto the merchant queue.</summary>
    [SubscribeLocalEvent]
    private void OnSubmitRequest(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeSubmitRequestMessage msg)
    {
        // Requests come from the crew request console only.
        if (!ent.Comp.RequestConsole)
            return;

        var item = msg.Item.Trim();
        if (item.Length == 0 || item.Length > 64)
            return;

        AddRequest(item, msg.Amount, "crew console");
    }

    /// <summary>Merchant clears a request once ordered/fulfilled (Eris order_requests).</summary>
    [SubscribeLocalEvent]
    private void OnCompleteRequest(Entity<OxydTradeConsoleComponent> ent, ref OxydTradeCompleteRequestMessage msg)
    {
        if (ent.Comp.RequestConsole)
            return;

        var id = msg.Id;
        var r = _requests.Find(x => x.Id == id);
        if (r == null)
            return;

        _requests.Remove(r);
        ent.Comp.Log.Add($"[Request] fulfilled: {r.Item} x{r.Amount}");
        PushState(ent);
    }

    /// <summary>Debug/testing hook (guild:submitrequest): enqueue a crew request as if a
    /// request console submitted it — BUI LineEdits can't be driven in the test client.</summary>
    public void DebugSubmitRequest(string item, int amount, string from)
    {
        AddRequest(item.Trim(), amount, from);
    }

    private void AddRequest(string item, int amount, string from)
    {
        _requests.Add(new Request
        {
            Id = _nextRequestId++,
            Item = item,
            Amount = Math.Clamp(amount, 1, 50),
            From = from,
        });

        var query = EntityQueryEnumerator<OxydTradeConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
            PushState((uid, comp));
    }
}
