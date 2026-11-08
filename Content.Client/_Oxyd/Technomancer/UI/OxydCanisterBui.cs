using Content.Client._Oxyd.UI;
using Content.Shared.Atmos.Piping.Binary.Components;
using Robust.Client.GameObjects;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Eris canister.tmpl reskin (Adapt) — dark panel + amber bars + pill links
/// over the stock canister state/messages (GasCanisterUiKey unchanged).
/// </summary>
public sealed class OxydCanisterBui : BoundUserInterface
{
    private OxydCanisterWindow? _window;

    public OxydCanisterBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydCanisterWindow>();
        _window.ValveToggled += on => SendMessage(new GasCanisterChangeReleaseValveMessage(on));
        _window.PressureSet += p => SendMessage(new GasCanisterChangeReleasePressureMessage(p));
        _window.Ejected += () => SendMessage(new GasCanisterHoldingTankEjectMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is GasCanisterBoundUserInterfaceState s)
            _window?.Update(s);
    }
}

public sealed class OxydCanisterWindow : FancyWindow
{
    public event Action<bool>? ValveToggled;
    public event Action<float>? PressureSet;
    public event Action? Ejected;

    private readonly ErisBar _pressure;
    private readonly Label _port;
    private readonly ErisLink _valve;
    private readonly Label _tank;
    private bool _valveOn;
    private float _release;

    public OxydCanisterWindow()
    {
        Title = "Canister";
        SetSize = new System.Numerics.Vector2(430, 300);
        var body = new ErisWindowBody();
        ContentsContainer.AddChild(body);
        var col = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        body.AddChild(col);

        col.AddChild(new Label { Text = "Canister Pressure" });
        _pressure = new ErisBar();
        col.AddChild(_pressure);

        _port = new Label { Text = "Port: disconnected" };
        col.AddChild(_port);

        var valveRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        valveRow.AddChild(new Label { Text = "Release valve: ", HorizontalExpand = true });
        _valve = new ErisLink("CLOSED");
        _valve.OnPressed += _ => { _valveOn = !_valveOn; ValveToggled?.Invoke(_valveOn); _valve.Text = _valveOn ? "OPEN" : "CLOSED"; if (_valveOn) _valve.SetSelectedLook(); else _valve.SetUnselectedLook(); };
        valveRow.AddChild(_valve);
        col.AddChild(valveRow);

        var pRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        pRow.AddChild(new Label { Text = "Release pressure: ", HorizontalExpand = true });
        foreach (var p in new[] { 101f, 500f, 1013f })
        {
            var l = new ErisLink($"{p:0}");
            var pv = p;
            l.OnPressed += _ => { _release = pv; PressureSet?.Invoke(pv); };
            pRow.AddChild(l);
        }
        col.AddChild(pRow);

        _tank = new Label { Text = "Holding tank: empty" };
        col.AddChild(_tank);

        var eject = new ErisLink("Eject holding tank", true);
        eject.OnPressed += _ => Ejected?.Invoke();
        col.AddChild(eject);
    }

    public void Update(GasCanisterBoundUserInterfaceState s)
    {
        _pressure.SetValue(Math.Clamp(s.CanisterPressure / 10130f, 0f, 1f),
            s.CanisterPressure < 1013f ? ErisNanoColors.BarGood : ErisNanoColors.BarAverage,
            $"{s.CanisterPressure:0} kPa");
        _port.Text = s.PortStatus ? "Port: connected" : "Port: disconnected";
        _tank.Text = s.TankPressure > 0 ? $"Holding tank: {s.TankPressure:0} kPa" : "Holding tank: empty";
    }
}
