using Content.Shared._Oxyd.Technomancer.Components;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Technomancer.UI;

public sealed class OxydBridgeBui : BoundUserInterface
{
    private OxydBridgeWindow? _window;

    public OxydBridgeBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydBridgeWindow>();
        _window.Toggle += () => SendMessage(new OxydPulsarBridgeToggleMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydPulsarBridgeState s)
            _window?.Update(s);
    }
}
