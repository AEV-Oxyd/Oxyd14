using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Economy;

public sealed class OxydAtmBoundUserInterface : BoundUserInterface
{
    private OxydAtmWindow? _window;

    public OxydAtmBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<OxydAtmWindow>();
        _window.OnLogin += (acct, pin) => SendMessage(new OxydAtmLoginMessage { AccountNumber = acct, Pin = pin });
        _window.OnLogout += () => SendMessage(new OxydAtmLogoutMessage());
        _window.OnWithdraw += amount => SendMessage(new OxydAtmWithdrawMessage { Amount = amount });
        _window.OnTransfer += (target, amount) =>
            SendMessage(new OxydAtmTransferMessage { TargetAccount = target, Amount = amount });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydAtmUiState s)
            _window?.UpdateState(s);
    }
}

public sealed class OxydAtmWindow : FancyWindow
{
    public event Action<int, int>? OnLogin;
    public event Action? OnLogout;
    public event Action<int>? OnWithdraw;
    public event Action<int, int>? OnTransfer;

    private readonly Label _status = new();
    private readonly Label _error = new() { Visible = false };
    private readonly LineEdit _account = new() { PlaceHolder = "Account #", HorizontalExpand = true };
    private readonly LineEdit _pin = new() { PlaceHolder = "PIN", MinWidth = 70 };
    private readonly LineEdit _target = new() { PlaceHolder = "Transfer target", HorizontalExpand = true };
    private readonly LineEdit _amount = new() { PlaceHolder = "Amount", MinWidth = 70 };
    private readonly BoxContainer _root;

    public OxydAtmWindow()
    {
        Title = "ATM";
        SetSize = new Vector2(380, 300);

        _root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 4,
            VerticalExpand = true,
            HorizontalExpand = true,
        };

        _root.AddChild(_status);
        _root.AddChild(_error);

        var loginRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        loginRow.AddChild(_account);
        loginRow.AddChild(_pin);
        var loginBtn = new Button { Text = "Login" };
        loginBtn.OnPressed += _ =>
        {
            int.TryParse(_account.Text, out var a);
            int.TryParse(_pin.Text, out var p);
            OnLogin?.Invoke(a, p);
        };
        var logoutBtn = new Button { Text = "Logout" };
        logoutBtn.OnPressed += _ => OnLogout?.Invoke();
        loginRow.AddChild(loginBtn);
        loginRow.AddChild(logoutBtn);
        _root.AddChild(loginRow);

        var withdrawRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var n in new[] { 100, 500, 1000 })
        {
            var btn = new Button { Text = $"Withdraw {n}" };
            var amount = n;
            btn.OnPressed += _ => OnWithdraw?.Invoke(amount);
            withdrawRow.AddChild(btn);
        }
        _root.AddChild(withdrawRow);

        var transferRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        transferRow.AddChild(_target);
        transferRow.AddChild(_amount);
        var transferBtn = new Button { Text = "Transfer" };
        transferBtn.OnPressed += _ =>
        {
            int.TryParse(_target.Text, out var t);
            int.TryParse(_amount.Text, out var a);
            OnTransfer?.Invoke(t, a);
        };
        transferRow.AddChild(transferBtn);
        _root.AddChild(transferRow);

        _root.AddChild(new Label { Text = "Insert a Credit stack on the machine to deposit." });
        ContentsContainer.AddChild(_root);
    }

    public void UpdateState(OxydAtmUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _status.Text = state.LoggedIn
            ? $"#{state.AccountNumber} {state.OwnerName} — {state.Balance}cr"
            : "Not logged in";
    }
}
