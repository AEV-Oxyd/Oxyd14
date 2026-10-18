using Content.Shared._Oxyd.AstersGuild;
using Content.Shared.Containers;
using EconomyBeaconRecv = Content.Shared._Oxyd.Economy.OxydTradeBeaconReceivingComponent;
using EconomyBeaconSend = Content.Shared._Oxyd.Economy.OxydTradeBeaconSendingComponent;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Content.Server._Oxyd.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Server backend for the Asters trade console — ports Eris SStrade
/// (code/controllers/subsystems/trade.dm): station tree with discovery, per-station goods
/// catalog w/ stock + prices, favor, rotating special offers with timers, cart -> order to
/// the receiving beacon, offer fulfillment via the sending beacon, transaction logs.
///
/// Beacon component types live in <c>Content.Shared._Oxyd.Economy</c> (shared with the
/// Eris economy+PDA port). Runtime cart/order state for PDA Trade Orders is owned by
/// <c>Content.Server._Oxyd.Economy.OxydTradeSystem</c> — this system is console-only.
/// </summary>
public sealed partial class OxydGuildTradeConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OxydBankSystem _bank = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    /// <summary>One sellable good (Eris goods_for_sale row).</summary>
    public sealed class Good
    {
        public EntProtoId Proto = string.Empty;
        public string Name = string.Empty;
        public string Category = string.Empty;
        public int Price;
        public int Stock;
    }

    /// <summary>One station on the trade tree.</summary>
    public sealed class Station
    {
        public string Name = string.Empty;
        public string Nick = string.Empty;
        public string Description = string.Empty;
        public bool Discovered;
        public int Favor;
        public int FavorMax = 100;
        public List<Good> Goods = new();
    }

    /// <summary>One rotating special offer (station buying an item type).</summary>
    public sealed class Offer
    {
        public int Id;
        public string Station = string.Empty;
        public string Want = string.Empty;      // EntProtoId to deliver
        public string Name = string.Empty;
        public int Price;
        public int Amount;
        public int AmountOriginal;
        public float SecondsLeft;
    }

    /// <summary>One crew order request on the merchant queue (Eris trade_orders).</summary>
    public sealed class Request
    {
        public int Id;
        public string Item = string.Empty;
        public int Amount;
        public string From = string.Empty;
    }

    private readonly List<Station> _stations = new();
    private readonly List<Offer> _offers = new();
    private readonly List<Request> _requests = new();
    private int _nextRequestId;
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();
        SeedStations();
        SeedOffers();

        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeSelectStationMessage>(OnSelectStation);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeSelectCategoryMessage>(OnSelectCategory);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeCartAdjustMessage>(OnCartAdjust);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeCreateOrderMessage>(OnCreateOrder);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeResetCartMessage>(OnResetCart);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeSendOfferMessage>(OnSendOffer);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeSaveCartMessage>(OnSaveCart);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeLoadCartMessage>(OnLoadCart);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeDeleteSavedCartMessage>(OnDeleteSavedCart);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeSubmitRequestMessage>(OnSubmitRequest);
        SubscribeLocalEvent<OxydTradeConsoleComponent, OxydTradeCompleteRequestMessage>(OnCompleteRequest);
        SubscribeLocalEvent<OxydTradeConsoleComponent, ComponentStartup>((u, c, _) => PushState(u, c));
        SubscribeLocalEvent<OxydTradeConsoleComponent, BoundUIOpenedEvent>((u, c, _) => PushState(u, c));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _stations.Clear();
        _offers.Clear();
        _requests.Clear();
        _nextRequestId = 0;
        SeedStations();
        SeedOffers();
    }

    /// <summary>The unified Guild department account in OxydEconomy — the same one
    /// ATMs, EFTPOS and trade beacons move money through.</summary>
    private Content.Shared._Oxyd.Economy.OxydMoneyAccountData? GuildAccount()
    {
        _economy.EnsureDepartmentsForRound();
        return _economy.FindDepartmentAccount(Content.Shared._Oxyd.Economy.OxydEconomyConstants.EmployerGuild);
    }

    /// <summary>Eris station catalog — modeled on TradeDestinations/Solnishko goods data.</summary>
    private void SeedStations()
    {
        if (_stations.Count != 0)
            return;
        _stations.Add(new Station
        {
            Name = "TTB 'Zarya'",
            Nick = "Zarya",
            Description = "Privet, this is the trade beacon 'Zarya'. We sell electronics, construction, and anything related to engineering!",
            Discovered = true,
            Goods =
            {
                new Good { Proto = "Crowbar", Name = "crowbar", Category = "Tools", Price = 24, Stock = 21 },
                new Good { Proto = "Screwdriver", Name = "screwdriver", Category = "Tools", Price = 24, Stock = 7 },
                new Good { Proto = "Shovel", Name = "shovel", Category = "Tools", Price = 24, Stock = 27 },
                new Good { Proto = "Wirecutter", Name = "wirecutters", Category = "Tools", Price = 24, Stock = 21 },
                new Good { Proto = "Wrench", Name = "wrench", Category = "Tools", Price = 24, Stock = 20 },
                new Good { Proto = "Welder", Name = "welding tool", Category = "Tools", Price = 24, Stock = 5 },
                new Good { Proto = "ToolboxEmergency", Name = "emergency toolbox", Category = "Tools", Price = 48, Stock = 16 },
                new Good { Proto = "ToolboxMechanical", Name = "mechanical toolbox", Category = "Tools", Price = 300, Stock = 17 },
                new Good { Proto = "ToolboxElectrical", Name = "electrical toolbox", Category = "Tools", Price = 372, Stock = 10 },
                new Good { Proto = "PowerCellSmall", Name = "cell 100S", Category = "Electronics", Price = 100, Stock = 20 },
                new Good { Proto = "PowerCellMedium", Name = "cell 600M", Category = "Electronics", Price = 300, Stock = 15 },
                new Good { Proto = "PowerCellHigh", Name = "cell 1500H", Category = "Electronics", Price = 700, Stock = 10 },
                new Good { Proto = "Multitool", Name = "multitool", Category = "Electronics", Price = 300, Stock = 8 },
            },
        });
        _stations.Add(new Station
        {
            Name = "IST 'Solnishko'",
            Nick = "Solnishko",
            Description = "The infamous Solnishko trading station — food, drink, and whatever else moves.",
            Discovered = true,
            Goods =
            {
                new Good { Proto = "FoodSnackChips", Name = "bag of chips", Category = "Supplies", Price = 40, Stock = 30 },
                new Good { Proto = "FoodSnackCnDs", Name = "CnD candies", Category = "Supplies", Price = 40, Stock = 25 },
                new Good { Proto = "DrinkWaterBottleFull", Name = "water bottle", Category = "Supplies", Price = 24, Stock = 40 },
                new Good { Proto = "DrinkCoffee", Name = "coffee", Category = "Supplies", Price = 36, Stock = 20 },
            },
        });
        _stations.Add(new Station
        {
            Name = "???",
            Nick = "unknown",
            Description = "An undiscovered trade station.",
            Discovered = false,
        });
    }

    /// <summary>Eris special offers — stations request goods at a markup for favor.</summary>
    private void SeedOffers()
    {
        _offers.Add(new Offer { Id = 0, Station = "Zarya", Want = "Crowbar", Name = "crowbar", Price = 2800, Amount = 5, AmountOriginal = 5, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 1, Station = "Zarya", Want = "Screwdriver", Name = "screwdriver", Price = 2800, Amount = 4, AmountOriginal = 4, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 2, Station = "Zarya", Want = "Shovel", Name = "shovel", Price = 2800, Amount = 4, AmountOriginal = 4, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 3, Station = "Solnishko", Want = "PowerCellSmall", Name = "cell 100S", Price = 5600, Amount = 10, AmountOriginal = 10, SecondsLeft = 300 });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTimer -= frameTime;
        foreach (var o in _offers)
            o.SecondsLeft -= frameTime;
        if (_updateTimer > 0)
            return;
        _updateTimer = 1f;
        var query = EntityQueryEnumerator<OxydTradeConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
            PushState(uid, comp);
    }

    private bool BeaconCheck(out string receiving, out string sending)
    {
        var recv = false; var send = false;
        var rq = EntityQueryEnumerator<EconomyBeaconRecv>();
        while (rq.MoveNext(out _, out _)) { recv = true; break; }
        var sq = EntityQueryEnumerator<EconomyBeaconSend>();
        while (sq.MoveNext(out _, out _)) { send = true; break; }
        receiving = recv ? "✓Receiving Beacon" : "✗No Receiving Beacon";
        sending = send ? "✓Sending Beacon" : "✗No Sending Beacon";
        return recv && send;
    }

    private void PushState(EntityUid uid, OxydTradeConsoleComponent comp)
    {
        if (!_ui.IsUiOpen(uid, OxydTradeUiKey.Key))
            return;
        BeaconCheck(out var recv, out var send);
        var s = new OxydTradeState
        {
            Balance = GuildAccount()?.Balance ?? 0,
            ReceivingBeacon = recv,
            SendingBeacon = send,
            SelectedStation = comp.SelectedStation,
            Category = comp.Category,
            CartCount = 0,
            CartTotal = 0,
            IsRequestConsole = comp.RequestConsole,
        };
        foreach (var r in _requests)
            s.Requests.Add(new OxydTradeRequestEntry
            {
                Id = r.Id, Item = r.Item, Amount = r.Amount, From = r.From,
            });
        foreach (var c in comp.SavedCarts)
            s.SavedCartNames.Add(c.Name);
        foreach (var st in _stations)
            s.Stations.Add(new OxydTradeStationEntry
            {
                Name = st.Discovered ? st.Name : "???",
                Nick = st.Nick,
                Description = st.Description,
                Discovered = st.Discovered,
                Favor = st.Favor,
                FavorMax = st.FavorMax,
                OfferSecondsLeft = (int)(_offers.Find(o => o.Station == st.Nick)?.SecondsLeft ?? 0),
            });
        if (comp.SelectedStation >= 0 && comp.SelectedStation < _stations.Count)
        {
            var st = _stations[comp.SelectedStation];
            s.SelectedName = st.Name;
            s.SelectedDesc = st.Description;
            var cats = new HashSet<string>();
            foreach (var g in st.Goods) cats.Add(g.Category);
            s.Categories.AddRange(cats);
            for (var i = 0; i < st.Goods.Count; i++)
            {
                var g = st.Goods[i];
                if (comp.Category != null && g.Category != comp.Category)
                    continue;
                s.Goods.Add(new OxydTradeGoodEntry
                {
                    Id = i, Name = g.Name, Price = g.Price, Available = g.Stock,
                    InCart = comp.Cart.TryGetValue(i, out var n) ? n : 0,
                });
            }
            foreach (var (id, n) in comp.Cart)
            {
                if (id < 0 || id >= st.Goods.Count) continue;
                var g = st.Goods[id];
                s.CartCount += n; s.CartTotal += n * g.Price;
                s.CartLines.Add($"{g.Name} x{n} — {n * g.Price}cr");
            }
        }
        foreach (var o in _offers)
            if (o.SecondsLeft > 0 && o.Amount > 0)
                s.Offers.Add(new OxydTradeOfferEntry
                {
                    Id = o.Id, Station = o.Station, Name = o.Name, Price = o.Price,
                    Amount = o.Amount, SecondsLeft = (int)o.SecondsLeft,
                });
        s.Log.AddRange(comp.Log);
        _ui.SetUiState(uid, OxydTradeUiKey.Key, s);
    }

    private void OnSelectStation(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSelectStationMessage msg)
    {
        if (msg.Index < 0 || msg.Index >= _stations.Count) return;
        if (!_stations[msg.Index].Discovered) return;
        comp.SelectedStation = msg.Index;
        comp.Category = null;
        comp.Cart.Clear();
        PushState(uid, comp);
    }

    private void OnSelectCategory(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSelectCategoryMessage msg)
    {
        comp.Category = msg.Category;
        PushState(uid, comp);
    }

    private void OnCartAdjust(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeCartAdjustMessage msg)
    {
        if (comp.SelectedStation < 0 || comp.SelectedStation >= _stations.Count) return;
        var st = _stations[comp.SelectedStation];
        if (msg.GoodId < 0 || msg.GoodId >= st.Goods.Count) return;
        var g = st.Goods[msg.GoodId];
        if (msg.Delta == 0)
        {
            comp.Cart.Remove(msg.GoodId);
        }
        else
        {
            comp.Cart.TryGetValue(msg.GoodId, out var n);
            n = Math.Clamp(n + msg.Delta, 0, g.Stock);
            if (n == 0) comp.Cart.Remove(msg.GoodId);
            else comp.Cart[msg.GoodId] = n;
        }
        PushState(uid, comp);
    }

    private void OnResetCart(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeResetCartMessage _)
    {
        comp.Cart.Clear();
        PushState(uid, comp);
    }

    /// <summary>Eris "Create Order": goods spawn on the receiving beacon, stock decrements,
    /// balance decrements, log line written.</summary>
    private void OnCreateOrder(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeCreateOrderMessage _)
    {
        // Crew request consoles only file requests — they can't spend the guild balance.
        if (comp.RequestConsole) return;
        if (comp.SelectedStation < 0 || comp.Cart.Count == 0) return;
        var st = _stations[comp.SelectedStation];

        // Charge only what can actually be delivered — stock is shared across
        // consoles, so anything short would otherwise vanish with no refund.
        var takes = new Dictionary<int, int>();
        var total = 0;
        foreach (var (id, n) in comp.Cart)
        {
            if (id < 0 || id >= st.Goods.Count) continue;
            var take = Math.Min(n, st.Goods[id].Stock);
            takes[id] = take;
            total += take * st.Goods[id].Price;
        }
        if (total <= 0)
            return;
        var guild = GuildAccount();
        if (guild == null || !_economy.TryAdjust(guild.AccountNumber, -total, $"Trade order {st.Nick}", "TradeConsole"))
            return;

        EntityUid? beacon = null;
        var bq = EntityQueryEnumerator<EconomyBeaconRecv>();
        while (bq.MoveNext(out var buid, out var _bc)) { beacon = buid; break; }
        var at = beacon != null
            ? Transform(beacon.Value).Coordinates
            : Transform(uid).Coordinates;

        // Eris delivers a shipping crate on the receiving beacon
        // (supply/shipping crates, order.dm) — goods go inside, not loose on the tile.
        var crate = Spawn("OxydTradeCrate", at);
        var delivered = new List<string>();
        foreach (var (id, n) in comp.Cart)
        {
            if (!takes.TryGetValue(id, out var take) || id < 0 || id >= st.Goods.Count) continue;
            var g = st.Goods[id];
            g.Stock -= take;
            for (var i = 0; i < take; i++)
            {
                var item = Spawn(g.Proto, at);
                if (TryComp(crate, out EntityStorageComponent? storage))
                    _container.Insert(item, storage.Contents);
            }
            delivered.Add($"{g.Name} x{take}");
        }
        comp.Log.Add($"[Order] {st.Nick}: {string.Join(", ", delivered)} — {total}cr");
        comp.Cart.Clear();
        PushState(uid, comp);
    }

    /// <summary>Eris offer send: consume matching entities near the sending beacon.</summary>
    private void OnSendOffer(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSendOfferMessage msg)
    {
        // Offers belong to the merchant console — a crew request console must not
        // spend or deliver goods on the guild's books.
        if (comp.RequestConsole) return;

        var o = _offers.Find(x => x.Id == msg.OfferId);
        if (o == null || o.SecondsLeft <= 0 || o.Amount <= 0) return;

        // Use the nearest sending beacon to THIS console — not the first beacon
        // anywhere in the world.
        EntityUid? beacon = null;
        var bestDist = float.MaxValue;
        var bq = EntityQueryEnumerator<EconomyBeaconSend>();
        var myPos = Transform(uid).WorldPosition;
        while (bq.MoveNext(out var buid, out _))
        {
            var d = (Transform(buid).WorldPosition - myPos).LengthSquared();
            if (d < bestDist)
            {
                bestDist = d;
                beacon = buid;
            }
        }
        if (beacon == null) return;

        var beaconPos = Transform(beacon.Value).Coordinates;
        var consumed = 0;
        var found = new List<EntityUid>();
        foreach (var ent in _lookup.GetEntitiesInRange(beaconPos, 1.5f))
        {
            if (consumed >= o.Amount) break;
            if (TryComp(ent, out MetaDataComponent? md) && md.EntityPrototype?.ID == o.Want)
            {
                found.Add(ent); consumed++;
            }
        }
        if (consumed <= 0) return;
        foreach (var e in found) Del(e);
        o.Amount -= consumed;
        // Pay pro-rata for partial deliveries — full price only on a full fill.
        var gained = o.AmountOriginal > 0
            ? (int)MathF.Floor(o.Price * consumed / (float)o.AmountOriginal)
            : 0;
        var guild = GuildAccount();
        if (guild != null && gained > 0)
            _economy.TryAdjust(guild.AccountNumber, gained, $"Trade offer {o.Station}", "TradeConsole");
        var st = _stations.Find(s => s.Nick == o.Station);
        if (st != null) st.Favor = Math.Min(st.Favor + consumed, st.FavorMax);
        comp.Log.Add($"[Offer] {o.Station}: {o.Name} x{consumed} — +{gained}cr, favor +{consumed}");
        PushState(uid, comp);
    }

    /// <summary>Eris saved_carts: snapshot the cart under a name (saved_carts list).</summary>
    private void OnSaveCart(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSaveCartMessage msg)
    {
        if (comp.Cart.Count == 0 || string.IsNullOrWhiteSpace(msg.Name))
            return;
        comp.SavedCarts.RemoveAll(c => c.Name == msg.Name);
        comp.SavedCarts.Add(new OxydSavedCart
        {
            Name = msg.Name.Trim(),
            Items = new Dictionary<int, int>(comp.Cart),
        });
        PushState(uid, comp);
    }

    /// <summary>Eris cart load: replace the cart with a saved one.</summary>
    private void OnLoadCart(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeLoadCartMessage msg)
    {
        if (msg.Index < 0 || msg.Index >= comp.SavedCarts.Count) return;
        comp.Cart.Clear();
        foreach (var (k, v) in comp.SavedCarts[msg.Index].Items)
            comp.Cart[k] = v;
        PushState(uid, comp);
    }

    private void OnDeleteSavedCart(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeDeleteSavedCartMessage msg)
    {
        if (msg.Index < 0 || msg.Index >= comp.SavedCarts.Count) return;
        comp.SavedCarts.RemoveAt(msg.Index);
        PushState(uid, comp);
    }

    /// <summary>Eris trade_orders: crew console pushes a request onto the merchant queue.
    /// The item is looked up by goods name across stations; unknown names still queue as
    /// free-text requests (Eris lets the merchant read anything).</summary>
    private void OnSubmitRequest(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSubmitRequestMessage msg)
    {
        // Requests come from the crew request console only.
        if (!comp.RequestConsole) return;
        var item = msg.Item.Trim();
        if (item.Length == 0 || item.Length > 64) return;
        _requests.Add(new Request
        {
            Id = _nextRequestId++,
            Item = item,
            Amount = Math.Clamp(msg.Amount, 1, 50),
            From = MetaData(uid).EntityPrototype?.ID == "OxydTradeRequestConsole" ? "crew console" : "console",
        });
        PushState(uid, comp);
    }

    /// <summary>Merchant clears a request once ordered/fulfilled (Eris order_requests).</summary>
    private void OnCompleteRequest(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeCompleteRequestMessage msg)
    {
        // Requests are completed on the merchant console, not the crew console.
        if (comp.RequestConsole) return;
        var r = _requests.Find(x => x.Id == msg.Id);
        if (r == null) return;
        _requests.Remove(r);
        comp.Log.Add($"[Request] fulfilled: {r.Item} x{r.Amount}");
        PushState(uid, comp);
    }

    /// <summary>Debug/testing hook (guild:submitrequest): enqueue a crew request as if a
    /// request console submitted it — BUI LineEdits can't be driven in the test client.</summary>
    public void DebugSubmitRequest(string item, int amount, string from)
    {
        _requests.Add(new Request
        {
            Id = _nextRequestId++,
            Item = item.Trim(),
            Amount = Math.Clamp(amount, 1, 50),
            From = from,
        });
        var q = EntityQueryEnumerator<OxydTradeConsoleComponent>();
        while (q.MoveNext(out var u, out var c))
            PushState(u, c);
    }

    [Dependency] private EntityLookupSystem _lookup = default!;
}
