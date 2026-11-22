using Content.Shared._Oxyd.Technomancer.Components;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Technomancer.UI;

public sealed class OxydBreakerBui : BoundUserInterface
{
    private OxydBreakerWindow? _window;

    public OxydBreakerBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydBreakerWindow>();
        _window.SetEnabled += on => SendMessage(new OxydBreakerToggleMessage(on));
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydBreakerState s)
            _window?.Update(s);
    }
}
