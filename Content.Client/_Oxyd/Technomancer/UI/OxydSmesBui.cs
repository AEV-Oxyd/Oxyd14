using Content.Shared.Power;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Client side of the Oxyd SMES window — Eris smes.tmpl skin over the stock
/// Battery protocol (Content.Server BatteryInterfaceSystem).
/// </summary>
public sealed class OxydSmesBui : BoundUserInterface
{
    private OxydSmesWindow? _window;

    public OxydSmesBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydSmesWindow>();
        _window.InputToggled += on => SendMessage(new BatterySetInputBreakerMessage(on));
        _window.OutputToggled += on => SendMessage(new BatterySetOutputBreakerMessage(on));
        _window.InputRateSet += rate => SendMessage(new BatterySetChargeRateMessage(rate));
        _window.OutputRateSet += rate => SendMessage(new BatterySetDischargeRateMessage(rate));
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is BatteryBuiState s)
            _window?.Update(s);
    }
}
