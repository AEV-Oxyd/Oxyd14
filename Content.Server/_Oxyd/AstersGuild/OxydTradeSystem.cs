using Content.Shared._Oxyd.AstersGuild;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Server backend for the Asters trade console — ports Eris SStrade
/// (code/controllers/subsystems/trade.dm): station tree with discovery, per-station goods
/// catalog w/ stock + prices, favor, rotating special offers with timers, cart -> order to
/// the receiving beacon, offer fulfillment via the sending beacon, transaction logs.
/// </summary>
public sealed partial class OxydTradeSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

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
        public float SecondsLeft;
    }

    private readonly List<Station> _stations = new();
    private readonly List<Offer> _offers = new();
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
        SubscribeLocalEvent<OxydTradeConsoleComponent, ComponentStartup>((u, c, _) => PushState(u, c));
        SubscribeLocalEvent<OxydTradeConsoleComponent, BoundUIOpenedEvent>((u, c, _) => PushState(u, c));
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
        _offers.Add(new Offer { Id = 0, Station = "Zarya", Want = "Crowbar", Name = "crowbar", Price = 2800, Amount = 5, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 1, Station = "Zarya", Want = "Screwdriver", Name = "screwdriver", Price = 2800, Amount = 4, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 2, Station = "Zarya", Want = "Shovel", Name = "shovel", Price = 2800, Amount = 4, SecondsLeft = 300 });
        _offers.Add(new Offer { Id = 3, Station = "Solnishko", Want = "PowerCellSmall", Name = "cell 100S", Price = 5600, Amount = 10, SecondsLeft = 300 });
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
        var rq = EntityQueryEnumerator<OxydTradeBeaconReceivingComponent>();
        while (rq.MoveNext(out _, out _)) { recv = true; break; }
        var sq = EntityQueryEnumerator<OxydTradeBeaconSendingComponent>();
        while (sq.MoveNext(out _, out _)) { send = true; break; }
        receiving = recv ? "✓Receiving Beacon: Cargo Bay (1, 1)" : "✗No Receiving Beacon";
        sending = send ? "✓Sending Beacon: Cargo Bay (1, 1)" : "✗No Sending Beacon";
        return recv && send;
    }

    private void PushState(EntityUid uid, OxydTradeConsoleComponent comp)
    {
        if (!_ui.IsUiOpen(uid, OxydTradeUiKey.Key))
            return;
        BeaconCheck(out var recv, out var send);
        var s = new OxydTradeState
        {
            Balance = comp.Balance,
            ReceivingBeacon = recv,
            SendingBeacon = send,
            SelectedStation = comp.SelectedStation,
            Category = comp.Category,
            CartCount = 0,
            CartTotal = 0,
        };
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
        if (comp.SelectedStation < 0 || comp.Cart.Count == 0) return;
        var st = _stations[comp.SelectedStation];
        var total = 0;
        foreach (var (id, n) in comp.Cart)
            if (id >= 0 && id < st.Goods.Count) total += n * st.Goods[id].Price;
        if (total > comp.Balance) return;

        EntityUid? beacon = null;
        var bq = EntityQueryEnumerator<OxydTradeBeaconReceivingComponent>();
        while (bq.MoveNext(out var buid, out var _bc)) { beacon = buid; break; }
        var at = beacon != null
            ? Transform(beacon.Value).Coordinates
            : Transform(uid).Coordinates;

        comp.Balance -= total;
        var delivered = new List<string>();
        foreach (var (id, n) in comp.Cart)
        {
            if (id < 0 || id >= st.Goods.Count) continue;
            var g = st.Goods[id];
            var take = Math.Min(n, g.Stock);
            g.Stock -= take;
            for (var i = 0; i < take; i++)
                Spawn(g.Proto, at);
            delivered.Add($"{g.Name} x{take}");
        }
        comp.Log.Add($"[Order] {st.Nick}: {string.Join(", ", delivered)} — {total}cr");
        comp.Cart.Clear();
        PushState(uid, comp);
    }

    /// <summary>Eris offer send: consume matching entities near the sending beacon.</summary>
    private void OnSendOffer(EntityUid uid, OxydTradeConsoleComponent comp, OxydTradeSendOfferMessage msg)
    {
        var o = _offers.Find(x => x.Id == msg.OfferId);
        if (o == null || o.SecondsLeft <= 0 || o.Amount <= 0) return;

        EntityUid? beacon = null;
        var bq = EntityQueryEnumerator<OxydTradeBeaconSendingComponent>();
        while (bq.MoveNext(out var buid, out _)) { beacon = buid; break; }
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
        var gained = o.Price;
        comp.Balance += gained;
        var st = _stations.Find(s => s.Nick == o.Station);
        if (st != null) st.Favor = Math.Min(st.Favor + consumed, st.FavorMax);
        comp.Log.Add($"[Offer] {o.Station}: {o.Name} x{consumed} — +{gained}cr, favor +{consumed}");
        PushState(uid, comp);
    }

    [Dependency] private EntityLookupSystem _lookup = default!;
}
