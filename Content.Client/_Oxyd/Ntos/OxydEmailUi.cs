using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydEmailUi : UIFragment
{
    private OxydEmailUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydEmailUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydEmailUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydEmailUiFragment : BoxContainer
{
    public event Action<OxydEmailUiMessageEvent>? OnMessage;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly LineEdit _login = new() { PlaceHolder = "name@nt.mail", MinWidth = 140 };
    private readonly LineEdit _password = new() { PlaceHolder = "password", MinWidth = 100 };
    private readonly LineEdit _to = new() { PlaceHolder = "to@nt.mail", HorizontalExpand = true };
    private readonly LineEdit _subject = new() { PlaceHolder = "subject", HorizontalExpand = true };
    private readonly LineEdit _body = new() { PlaceHolder = "message body", HorizontalExpand = true };
    private readonly BoxContainer _inbox = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly Label _detail = new();

    public OxydEmailUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 360;

        AddChild(_banner);
        AddChild(_error);

        var auth = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        auth.AddChild(_login);
        auth.AddChild(_password);
        var loginBtn = new Button { Text = "Login" };
        loginBtn.OnPressed += _ => Emit(OxydEmailUiAction.Login);
        var regBtn = new Button { Text = "Register" };
        regBtn.OnPressed += _ => Emit(OxydEmailUiAction.Register);
        var logoutBtn = new Button { Text = "Logout" };
        logoutBtn.OnPressed += _ => Emit(OxydEmailUiAction.Logout);
        auth.AddChild(loginBtn);
        auth.AddChild(regBtn);
        auth.AddChild(logoutBtn);
        AddChild(auth);

        AddChild(new ScrollContainer { VerticalExpand = true, MinHeight = 100, Children = { _inbox } });
        AddChild(_detail);

        AddChild(_to);
        AddChild(_subject);
        AddChild(_body);
        var send = new Button { Text = "Send" };
        send.OnPressed += _ => Emit(OxydEmailUiAction.Send);
        AddChild(send);
        var refresh = new Button { Text = "Refresh inbox" };
        refresh.OnPressed += _ => Emit(OxydEmailUiAction.Refresh);
        AddChild(refresh);
    }

    public void UpdateState(OxydEmailUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = state.LoggedIn
            ? $"{state.Login} — {state.Inbox.Count} messages{(state.Status is { } s ? $" ({s})" : string.Empty)}"
            : "NT Mail — not logged in";

        _inbox.RemoveAllChildren();
        foreach (var m in state.Inbox)
        {
            var id = m.Id;
            var btn = new Button
            {
                Text = $"{(m.Read ? " " : "*")}{m.From}: {m.Subject}",
                HorizontalExpand = true,
            };
            btn.OnPressed += _ =>
            {
                OnMessage?.Invoke(new OxydEmailUiMessageEvent(OxydEmailUiAction.Open) { MessageId = id });
            };
            _inbox.AddChild(btn);
        }

        if (state.Active is { } d)
            _detail.Text = $"From: {d.From}\nTo: {d.To}\n{d.Subject}\n\n{d.Body}";
        else
            _detail.Text = string.Empty;
    }

    private void Emit(OxydEmailUiAction action)
    {
        OnMessage?.Invoke(new OxydEmailUiMessageEvent(action)
        {
            Login = _login.Text ?? string.Empty,
            Password = _password.Text ?? string.Empty,
            To = _to.Text ?? string.Empty,
            Subject = _subject.Text ?? string.Empty,
            Body = _body.Text ?? string.Empty,
        });
    }
}
