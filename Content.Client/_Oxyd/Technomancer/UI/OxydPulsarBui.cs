using Content.Shared._Oxyd.Technomancer.Components;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Technomancer.UI;

/// Client side of the Oxyd pulsar console (Eris pulsar.tmpl skin).
public sealed class OxydPulsarBui : BoundUserInterface
{
    private OxydPulsarWindow? _window;

    public OxydPulsarBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydPulsarWindow>();
        _window.Move += dir => SendMessage(new OxydPulsarMoveMessage(dir));
        _window.ShieldSet += pct => SendMessage(new OxydPulsarShieldMessage(pct));
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydPulsarState s)
            _window?.Update(s);
    }
}
