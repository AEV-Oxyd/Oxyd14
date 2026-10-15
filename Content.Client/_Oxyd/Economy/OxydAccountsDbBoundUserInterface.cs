using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Economy;

public sealed class OxydAccountsDbBoundUserInterface : BoundUserInterface
{
    private OxydAccountsDbWindow? _window;

    public OxydAccountsDbBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<OxydAccountsDbWindow>();
        _window.OnSelect += n => SendMessage(new OxydAccountsDbSelectMessage { AccountNumber = n });
        _window.OnBack += () => SendMessage(new OxydAccountsDbBackMessage());
        _window.OnBeginCreate += () => SendMessage(new OxydAccountsDbBeginCreateMessage());
        _window.OnCreate += (name, funds) =>
            SendMessage(new OxydAccountsDbCreateMessage { OwnerName = name, StartingFunds = funds });
        _window.OnToggleSuspend += () => SendMessage(new OxydAccountsDbToggleSuspendMessage());
        _window.OnAdjust += d => SendMessage(new OxydAccountsDbAdjustMessage { Delta = d });
        _window.OnRevoke += () => SendMessage(new OxydAccountsDbRevokePayrollMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydAccountsDbUiState s)
            _window?.UpdateState(s);
    }
}

public sealed class OxydAccountsDbWindow : FancyWindow
{
    public event Action<int>? OnSelect;
    public event Action? OnBack;
    public event Action? OnBeginCreate;
    public event Action<string, int>? OnCreate;
    public event Action? OnToggleSuspend;
    public event Action<int>? OnAdjust;
    public event Action? OnRevoke;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly BoxContainer _body = new()
    {
        Orientation = LayoutOrientation.Vertical,
        SeparationOverride = 4,
        VerticalExpand = true,
        HorizontalExpand = true,
    };

    private readonly LineEdit _holder = new() { PlaceHolder = "Holder name", HorizontalExpand = true };
    private readonly LineEdit _funds = new() { PlaceHolder = "Starting funds", MinWidth = 90 };
    private readonly LineEdit _adjust = new() { PlaceHolder = "Δ credits", MinWidth = 90 };

    public OxydAccountsDbWindow()
    {
        Title = "Accounts uplink";
        SetSize = new Vector2(440, 480);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 4,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        root.AddChild(_banner);
        root.AddChild(_error);
        root.AddChild(_body);
        ContentsContainer.AddChild(root);
    }

    public void UpdateState(OxydAccountsDbUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = state.Status ?? (state.HasAccess ? "Authorized" : "Insert HoP/Captain ID");

        _body.RemoveAllChildren();

        if (!state.HasAccess)
        {
            _body.AddChild(new Label { Text = "Access denied." });
            return;
        }

        if (state.Creating)
        {
            _body.AddChild(new Label { Text = "Create personal account" });
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            row.AddChild(_holder);
            row.AddChild(_funds);
            var create = new Button { Text = "Finalize" };
            create.OnPressed += _ =>
            {
                int.TryParse(_funds.Text, out var f);
                OnCreate?.Invoke(_holder.Text ?? string.Empty, f);
            };
            row.AddChild(create);
            _body.AddChild(row);
            var back = new Button { Text = "Back" };
            back.OnPressed += _ => OnBack?.Invoke();
            _body.AddChild(back);
            return;
        }

        if (state.DetailAccount is { } detail)
        {
            _body.AddChild(new Label
            {
                Text =
                    $"#{detail} {state.DetailOwner}\n{state.DetailBalance}cr · wage {state.DetailWage} · {state.DetailEmployer ?? "—"}\n{(state.DetailSuspended ? "SUSPENDED" : "Active")}",
            });

            var actions = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var sus = new Button { Text = state.DetailSuspended ? "Reinstate" : "Suspend" };
            sus.OnPressed += _ => OnToggleSuspend?.Invoke();
            actions.AddChild(sus);
            var revoke = new Button { Text = "Revoke payroll" };
            revoke.OnPressed += _ => OnRevoke?.Invoke();
            actions.AddChild(revoke);
            _body.AddChild(actions);

            var adjRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            adjRow.AddChild(_adjust);
            var add = new Button { Text = "Add" };
            add.OnPressed += _ =>
            {
                int.TryParse(_adjust.Text, out var d);
                OnAdjust?.Invoke(Math.Abs(d));
            };
            var rem = new Button { Text = "Remove" };
            rem.OnPressed += _ =>
            {
                int.TryParse(_adjust.Text, out var d);
                OnAdjust?.Invoke(-Math.Abs(d));
            };
            adjRow.AddChild(add);
            adjRow.AddChild(rem);
            _body.AddChild(adjRow);

            foreach (var tx in state.Transactions)
            {
                _body.AddChild(new Label
                {
                    Text = $"{tx.Amount:+#;-#;0} {tx.Purpose} @ {tx.SourceTerminal}",
                });
            }

            var back = new Button { Text = "Back to list" };
            back.OnPressed += _ => OnBack?.Invoke();
            _body.AddChild(back);
            return;
        }

        var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var newBtn = new Button { Text = "New account" };
        newBtn.OnPressed += _ => OnBeginCreate?.Invoke();
        top.AddChild(newBtn);
        top.AddChild(new Label { Text = $"{state.Accounts.Count} accounts" });
        _body.AddChild(top);

        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 300 };
        var list = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        foreach (var acc in state.Accounts)
        {
            var btn = new Button
            {
                Text =
                    $"#{acc.AccountNumber} {acc.OwnerName} — {acc.Balance}cr{(acc.Suspended ? " [SUS]" : "")}{(acc.IsDepartment ? " [DEPT]" : "")}",
                HorizontalExpand = true,
                StyleClasses = { "OpenRight" },
            };
            var number = acc.AccountNumber;
            btn.OnPressed += _ => OnSelect?.Invoke(number);
            list.AddChild(btn);
        }

        scroll.AddChild(list);
        _body.AddChild(scroll);
    }
}
