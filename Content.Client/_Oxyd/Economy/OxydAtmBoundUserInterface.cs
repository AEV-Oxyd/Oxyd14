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
    private readonly OptionButton _target = new() { HorizontalExpand = true };
    private readonly LineEdit _withdrawAmount = new() { PlaceHolder = "Withdraw amount", HorizontalExpand = true };
    private readonly LineEdit _amount = new() { PlaceHolder = "Transfer amount", MinWidth = 90 };
    private int _selectedTarget;
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
        withdrawRow.AddChild(_withdrawAmount);
        var withdrawBtn = new Button { Text = "Withdraw" };
        withdrawBtn.OnPressed += _ =>
        {
            int.TryParse(_withdrawAmount.Text, out var amount);
            OnWithdraw?.Invoke(amount);
        };
        withdrawRow.AddChild(withdrawBtn);
        _root.AddChild(withdrawRow);

        var transferRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _target.OnItemSelected += args =>
        {
            _target.SelectId(args.Id);
            _selectedTarget = args.Id;
        };
        transferRow.AddChild(_target);
        transferRow.AddChild(_amount);
        var transferBtn = new Button { Text = "Transfer" };
        transferBtn.OnPressed += _ =>
        {
            int.TryParse(_amount.Text, out var a);
            OnTransfer?.Invoke(_selectedTarget, a);
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

        var selected = _selectedTarget;
        _target.Clear();
        var index = 0;
        foreach (var account in state.Accounts)
        {
            _target.AddItem($"#{account.Number} {account.Name}", account.Number);
            if (account.Number == selected)
                index = _target.ItemCount - 1;
        }

        if (_target.ItemCount > 0)
        {
            _target.Select(index);
            _selectedTarget = (int)_target.SelectedId;
        }
    }
}
