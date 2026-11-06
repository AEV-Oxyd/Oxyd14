using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Economy;

public sealed class OxydTradeBeaconBoundUserInterface : BoundUserInterface
{
    private OxydTradeBeaconWindow? _window;

    public OxydTradeBeaconBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<OxydTradeBeaconWindow>();
        _window.OnLink += acct => SendMessage(new OxydTradeBeaconLinkMessage { AccountNumber = acct });
        _window.OnExport += () => SendMessage(new OxydTradeBeaconExportMessage());
        _window.OnPurchase += orderId => SendMessage(new OxydTradeBeaconPurchaseMessage { OrderId = orderId });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydTradeBeaconUiState s)
            _window?.UpdateState(s);
    }
}

public sealed class OxydTradeBeaconWindow : FancyWindow
{
    public event Action<int>? OnLink;
    public event Action? OnExport;
    public event Action<string>? OnPurchase;

    private readonly Label _status = new();
    private readonly Label _error = new() { Visible = false };
    private readonly LineEdit _account = new() { PlaceHolder = "Account #", MinWidth = 100 };
    private readonly LineEdit _order = new() { PlaceHolder = "order_N", MinWidth = 100 };
    private readonly Button _export = new() { Text = "Export offers" };
    private readonly Button _purchase = new() { Text = "Purchase order" };
    private readonly BoxContainer _recvRow;

    public OxydTradeBeaconWindow()
    {
        Title = "Trade beacon";
        SetSize = new Vector2(380, 220);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        root.AddChild(_status);
        root.AddChild(_error);

        var linkRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        linkRow.AddChild(_account);
        var link = new Button { Text = "Link account" };
        link.OnPressed += _ =>
        {
            int.TryParse(_account.Text, out var a);
            OnLink?.Invoke(a);
        };
        linkRow.AddChild(link);
        root.AddChild(linkRow);

        _export.OnPressed += _ => OnExport?.Invoke();
        root.AddChild(_export);

        _recvRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _recvRow.AddChild(_order);
        _purchase.OnPressed += _ => OnPurchase?.Invoke(_order.Text ?? string.Empty);
        _recvRow.AddChild(_purchase);
        root.AddChild(_recvRow);

        ContentsContainer.AddChild(root);
    }

    public void UpdateState(OxydTradeBeaconUiState state)
    {
        Title = state.Sending ? "Sending trade beacon" : "Receiving trade beacon";
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _status.Text = (state.LinkedAccount is { } a ? $"Linked #{a}. " : "No linked account (defaults Guild). ")
                       + state.Status;
        _export.Visible = state.Sending;
        _export.Disabled = !state.Ready;
        _recvRow.Visible = !state.Sending;
    }
}
