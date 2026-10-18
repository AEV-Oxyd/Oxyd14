using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydSignallerUi : UIFragment
{
    private OxydSignallerUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydSignallerUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydSignallerUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydSignallerUiFragment : BoxContainer
{
    public event Action<OxydSignallerUiMessageEvent>? OnMessage;

    private readonly Label _status = new();
    private readonly Label _error = new() { Visible = false };
    private readonly Button _fire = new() { Text = "Transmit signal" };

    public OxydSignallerUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        HorizontalExpand = true;
        MinHeight = 160;
        Margin = new Thickness(4);

        AddChild(_status);
        AddChild(_error);
        _fire.OnPressed += _ => OnMessage?.Invoke(new OxydSignallerUiMessageEvent(OxydSignallerUiAction.Fire));
        AddChild(_fire);
        var refresh = new Button { Text = "Refresh" };
        refresh.OnPressed += _ => OnMessage?.Invoke(new OxydSignallerUiMessageEvent(OxydSignallerUiAction.Refresh));
        AddChild(refresh);
    }

    public void UpdateState(OxydSignallerUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _status.Text = state.Status
                       ?? (state.HasHardware
                           ? $"Signaller ready — net range {state.NetworkRange}m. Link devices, then transmit."
                           : "Install Oxyd signaller hardware in the peripheral bay.");
        _fire.Disabled = !state.HasHardware;
    }
}
