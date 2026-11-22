using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydNtChatUi : UIFragment
{
    private OxydNtChatUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydNtChatUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydNtChatUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydNtChatUiFragment : BoxContainer
{
    public event Action<OxydNtChatUiMessageEvent>? OnMessage;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly LineEdit _name = new() { PlaceHolder = "username", MinWidth = 120 };
    private readonly LineEdit _channel = new() { PlaceHolder = "new channel name", MinWidth = 140 };
    private readonly LineEdit _message = new() { PlaceHolder = "message", HorizontalExpand = true };
    private readonly BoxContainer _channels = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly Label _log = new();

    public OxydNtChatUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 360;

        AddChild(_banner);
        AddChild(_error);

        var nameRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        nameRow.AddChild(_name);
        var setName = new Button { Text = "Set name" };
        setName.OnPressed += _ => Emit(OxydNtChatUiAction.SetName, _name.Text);
        nameRow.AddChild(setName);
        AddChild(nameRow);

        AddChild(new Label { Text = "Channels" });
        AddChild(new ScrollContainer { MinHeight = 80, Children = { _channels } });

        var createRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        createRow.AddChild(_channel);
        var create = new Button { Text = "Create" };
        create.OnPressed += _ => Emit(OxydNtChatUiAction.Create, _channel.Text);
        var leave = new Button { Text = "Leave" };
        leave.OnPressed += _ => Emit(OxydNtChatUiAction.Leave);
        var refresh = new Button { Text = "Refresh" };
        refresh.OnPressed += _ => Emit(OxydNtChatUiAction.Refresh);
        createRow.AddChild(create);
        createRow.AddChild(leave);
        createRow.AddChild(refresh);
        AddChild(createRow);

        AddChild(new ScrollContainer { VerticalExpand = true, MinHeight = 120, Children = { _log } });

        var speakRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        speakRow.AddChild(_message);
        var speak = new Button { Text = "Send" };
        speak.OnPressed += _ => Emit(OxydNtChatUiAction.Speak, _message.Text);
        speakRow.AddChild(speak);
        AddChild(speakRow);
    }

    public void UpdateState(OxydNtChatUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = state.ActiveChannelTitle is { } t
            ? $"{state.Username} @ {t}"
            : $"{state.Username} — no channel";

        _channels.RemoveAllChildren();
        foreach (var ch in state.Channels)
        {
            var id = ch.Id;
            var btn = new Button { Text = $"{ch.Title} ({ch.Users})", HorizontalExpand = true };
            btn.OnPressed += _ =>
            {
                OnMessage?.Invoke(new OxydNtChatUiMessageEvent(OxydNtChatUiAction.Join) { ChannelId = id });
            };
            _channels.AddChild(btn);
        }

        _log.Text = state.Messages.Count == 0
            ? "(no messages)"
            : string.Join('\n', state.Messages);
    }

    private void Emit(OxydNtChatUiAction action, string? text = null)
    {
        OnMessage?.Invoke(new OxydNtChatUiMessageEvent(action) { Text = text ?? string.Empty });
    }
}
