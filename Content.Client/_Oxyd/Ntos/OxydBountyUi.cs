using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydBountyUi : UIFragment
{
    private OxydBountyUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydBountyUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydBountyUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydBountyUiFragment : BoxContainer
{
    public event Action<OxydBountyUiMessageEvent>? OnMessage;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly BoxContainer _body = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };

    public OxydBountyUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 320;

        AddChild(_banner);
        AddChild(_error);
        var refresh = new Button { Text = "Refresh" };
        refresh.OnPressed += _ => OnMessage?.Invoke(new OxydBountyUiMessageEvent(OxydBountyUiAction.Refresh));
        AddChild(refresh);
        AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            Children = { _body },
        });
    }

    public void UpdateState(OxydBountyUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = string.IsNullOrEmpty(state.StationName)
            ? "Bounty board"
            : $"{state.StationName} bounty board";

        _body.RemoveAllChildren();
        _body.AddChild(new Label { Text = $"Active ({state.Active.Count})" });
        foreach (var b in state.Active)
        {
            _body.AddChild(new Label
            {
                Text = $"[{b.Id}] {b.Reward}cr — {b.Description}",
            });
        }

        _body.AddChild(new Label { Text = $"History ({state.History.Count})" });
        foreach (var h in state.History)
            _body.AddChild(new Label { Text = $"{h.Id}: {h.Status}" });
    }
}
