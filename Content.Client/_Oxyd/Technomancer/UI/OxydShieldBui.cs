using Content.Shared._Oxyd.Technomancer.Components;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Technomancer.UI;

public sealed class OxydShieldBui : BoundUserInterface
{
    private OxydShieldWindow? _window;

    public OxydShieldBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydShieldWindow>();
        _window.Toggle += () => SendMessage(new OxydShieldToggleMessage());
        _window.ModeSet += (id, on) => SendMessage(new OxydShieldModeMessage(id, on));
        _window.FieldSizeSet += delta =>
        {
            if (_window != null)
                SendMessage(new OxydShieldFieldSizeMessage(_window.CurrentFieldSize + delta));
        };
        _window.InputCapSet += delta =>
        {
            if (_window != null)
                SendMessage(new OxydShieldInputCapMessage(Math.Max(0f, _window.CurrentInputCap + delta)));
        };
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydShieldState s)
        {
            if (_window != null)
            {
                _window.CurrentFieldSize = s.FieldSize;
                _window.CurrentInputCap = s.InputCap;
                _window.Update(s);
            }
        }
    }
}
