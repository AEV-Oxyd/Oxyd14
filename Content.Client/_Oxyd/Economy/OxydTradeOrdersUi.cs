using System.Globalization;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Economy;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Economy;

public sealed partial class OxydTradeOrdersUi : UIFragment
{
    private OxydTradeOrdersUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydTradeOrdersUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydTradeOrdersUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydTradeOrdersUiFragment : BoxContainer
{
    public event Action<OxydTradeOrdersUiMessageEvent>? OnMessage;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly BoxContainer _body = new()
    {
        Orientation = LayoutOrientation.Vertical,
        SeparationOverride = 3,
        VerticalExpand = true,
        HorizontalExpand = true,
    };

    private readonly LineEdit _account = new() { PlaceHolder = "Account #", MinWidth = 90 };
    private readonly LineEdit _pin = new() { PlaceHolder = "PIN", MinWidth = 60 };
    private readonly LineEdit _reason = new() { PlaceHolder = "Order reason", HorizontalExpand = true };
    private readonly LineEdit _orderId = new() { PlaceHolder = "order_N", MinWidth = 100 };

    public OxydTradeOrdersUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 360;

        AddChild(_banner);
        AddChild(_error);

        var login = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        login.AddChild(_account);
        login.AddChild(_pin);
        var loginBtn = new Button { Text = "Login" };
        loginBtn.OnPressed += _ => Emit(OxydTradeUiAction.Login);
        var logoutBtn = new Button { Text = "Logout" };
        logoutBtn.OnPressed += _ => Emit(OxydTradeUiAction.Logout);
        login.AddChild(loginBtn);
        login.AddChild(logoutBtn);
        AddChild(login);

        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 2 };
        foreach (var tab in new[]
                 {
                     OxydTradeUiTab.Stations, OxydTradeUiTab.Goods, OxydTradeUiTab.Cart,
                     OxydTradeUiTab.Orders, OxydTradeUiTab.Offers, OxydTradeUiTab.Logs,
                 })
        {
            var t = tab;
            var btn = new Button { Text = tab.ToString() };
            btn.OnPressed += _ =>
            {
                var ev = new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.SetTab) { Tab = t };
                OnMessage?.Invoke(ev);
            };
            tabs.AddChild(btn);
        }

        AddChild(tabs);
        AddChild(_body);
    }

    public void UpdateState(OxydTradeOrdersUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = state.LoggedIn
            ? $"#{state.AccountNumber} {state.OwnerName} — {state.Balance}cr{(state.IsGuild ? " [GUILD]" : "")} | {state.Tab}"
              + (state.Status != null ? $" — {state.Status}" : string.Empty)
            : "Trade Orders — log in with Guild or personal account";

        _body.RemoveAllChildren();
        if (!state.LoggedIn)
        {
            _body.AddChild(new Label { Text = "Login required to browse and order." });
            return;
        }

        switch (state.Tab)
        {
            case OxydTradeUiTab.Stations:
                foreach (var st in state.Stations)
                {
                    var btn = new Button
                    {
                        Text = $"{st.Name} — {st.UniqueGoods} goods, {st.ActiveOffers} offers (favor {st.Favor})",
                        HorizontalExpand = true,
                    };
                    var id = st.Id;
                    btn.OnPressed += _ =>
                    {
                        OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.SelectStation)
                        {
                            StationId = id,
                        });
                    };
                    _body.AddChild(btn);
                    if (!string.IsNullOrWhiteSpace(st.Description))
                        _body.AddChild(new Label { Text = "  " + st.Description });
                }

                break;

            case OxydTradeUiTab.Goods:
                _body.AddChild(new Label
                {
                    Text = state.SelectedStationId == null
                        ? "Select a station first."
                        : $"Station {state.SelectedStationId}",
                });
                foreach (var g in state.Goods)
                {
                    var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
                    row.AddChild(new Label
                    {
                        Text = $"[{g.Category}] {g.Name} — {g.Price}cr (stock {g.Stock})",
                        HorizontalExpand = true,
                    });
                    var add = new Button { Text = "+1" };
                    var station = g.StationId;
                    var product = g.Product;
                    add.OnPressed += _ =>
                    {
                        OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.AddToCart)
                        {
                            StationId = station,
                            Product = product,
                            Amount = 1,
                        });
                    };
                    row.AddChild(add);
                    _body.AddChild(row);
                }

                break;

            case OxydTradeUiTab.Cart:
                _body.AddChild(new Label { Text = $"Cart total: {state.CartTotal}cr (+fee if not Guild)" });
                foreach (var line in state.Cart)
                {
                    var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
                    row.AddChild(new Label
                    {
                        Text = $"{line.Amount}x {line.Name} @ {line.Price} = {line.Amount * line.Price}cr",
                        HorizontalExpand = true,
                    });
                    var rem = new Button { Text = "-1" };
                    var sid = line.StationId;
                    var prod = line.Product;
                    rem.OnPressed += _ =>
                    {
                        OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.RemoveFromCart)
                        {
                            StationId = sid,
                            Product = prod,
                            Amount = 1,
                        });
                    };
                    row.AddChild(rem);
                    _body.AddChild(row);
                }

                var placeRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
                placeRow.AddChild(_reason);
                var place = new Button { Text = "Place order" };
                place.OnPressed += _ =>
                {
                    OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.PlaceOrder)
                    {
                        Reason = _reason.Text,
                    });
                };
                var clear = new Button { Text = "Clear" };
                clear.OnPressed += _ => OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.ClearCart));
                placeRow.AddChild(place);
                placeRow.AddChild(clear);
                _body.AddChild(placeRow);
                break;

            case OxydTradeUiTab.Orders:
                foreach (var o in state.Orders)
                {
                    _body.AddChild(new Label
                    {
                        Text =
                            $"{o.OrderId} {(o.Fulfilled ? "[DONE]" : "[OPEN]")} {o.RequestingName} cost {o.Cost}+fee {o.Fee}\n  {o.ContentsSummary}\n  {o.Reason}",
                    });
                    if (!o.Fulfilled)
                    {
                        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
                        var oid = o.OrderId;
                        var fulfill = new Button { Text = "Fulfill @ recv beacon" };
                        fulfill.OnPressed += _ =>
                        {
                            OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.FulfillOrder)
                            {
                                OrderId = oid,
                            });
                        };
                        var cancel = new Button { Text = "Cancel" };
                        cancel.OnPressed += _ =>
                        {
                            OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.CancelOrder)
                            {
                                OrderId = oid,
                            });
                        };
                        row.AddChild(fulfill);
                        row.AddChild(cancel);
                        _body.AddChild(row);
                    }
                }

                break;

            case OxydTradeUiTab.Offers:
                foreach (var o in state.Offers)
                {
                    _body.AddChild(new Label
                    {
                        Text = $"{o.StationName}: {o.AmountRemaining}x {o.Name} → {o.Price}cr (proto {o.Product})",
                    });
                }

                var export = new Button { Text = "Fulfill offers @ send beacon" };
                export.OnPressed += _ =>
                    OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.FulfillOffers));
                _body.AddChild(export);
                break;

            case OxydTradeUiTab.Logs:
                foreach (var l in state.Logs)
                {
                    _body.AddChild(new Label
                    {
                        Text = $"{l.Id} {l.Type} {l.Account} {l.Paid}cr — {l.Contents}",
                    });
                }

                break;
        }

        // Always show beacon purchase helper on Orders tab bottom
        if (state.Tab == OxydTradeUiTab.Orders)
        {
            var help = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            help.AddChild(_orderId);
            var buy = new Button { Text = "Purchase order id @ recv" };
            buy.OnPressed += _ =>
            {
                OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(OxydTradeUiAction.FulfillOrder)
                {
                    OrderId = _orderId.Text,
                });
            };
            help.AddChild(buy);
            _body.AddChild(help);
        }
    }

    private void Emit(OxydTradeUiAction action)
    {
        int.TryParse(_account.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var account);
        int.TryParse(_pin.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pin);
        OnMessage?.Invoke(new OxydTradeOrdersUiMessageEvent(action)
        {
            AccountNumber = account,
            Pin = pin,
        });
    }
}
