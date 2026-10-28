using Content.Client._Oxyd.UI;
using Content.Shared.Atmos.Monitor.Components;
using Robust.Client.GameObjects;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Eris air_alarm.tmpl Adapt — Eris-skin pane over the stock AirAlarmUiKey
/// state (mode/status/pressure/temperature readout + AUTO pill); the deep
/// per-vent threshold editor stays stock via the regular alarm console.
/// </summary>
public sealed class OxydAirAlarmBui : BoundUserInterface
{
    private OxydAirAlarmWindow? _window;

    public OxydAirAlarmBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydAirAlarmWindow>();
        _window.ModeSet += m => SendMessage(new AirAlarmUpdateAlarmModeMessage(m));
        _window.AutoSet += on => SendMessage(new AirAlarmUpdateAutoModeMessage(on));
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is AirAlarmUIState s)
            _window?.Update(s);
    }
}

public sealed class OxydAirAlarmWindow : FancyWindow
{
    public event Action<AirAlarmMode>? ModeSet;
    public event Action<bool>? AutoSet;

    private readonly Label _mode;
    private readonly Label _alarm;
    private readonly Label _env;
    private readonly ErisLink _auto;

    public OxydAirAlarmWindow()
    {
        Title = "Air Alarm";
        SetSize = new System.Numerics.Vector2(430, 280);
        var body = new ErisWindowBody();
        ContentsContainer.AddChild(body);
        var col = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        body.AddChild(col);

        col.AddChild(new Label { Text = "Area status" });
        _alarm = new Label();
        col.AddChild(_alarm);
        _env = new Label();
        col.AddChild(_env);
        _mode = new Label();
        col.AddChild(_mode);

        var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        modes.AddChild(new Label { Text = "Mode: ", HorizontalExpand = true });
        foreach (var m in new[] { AirAlarmMode.Filtering, AirAlarmMode.WideFiltering, AirAlarmMode.Panic, AirAlarmMode.Fill })
        {
            var l = new ErisLink(m.ToString());
            var mv = m;
            l.OnPressed += _ => ModeSet?.Invoke(mv);
            modes.AddChild(l);
        }
        col.AddChild(modes);

        var autoRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        autoRow.AddChild(new Label { Text = "Auto: ", HorizontalExpand = true });
        _auto = new ErisLink("ON");
        var on = true;
        _auto.OnPressed += _ => { on = !on; _auto.Text = on ? "ON" : "OFF"; AutoSet?.Invoke(on); };
        autoRow.AddChild(_auto);
        col.AddChild(autoRow);
    }

    public void Update(AirAlarmUIState s)
    {
        _alarm.Text = $"Alarm: {s.AlarmType}";
        _env.Text = $"Pressure {s.PressureAverage:0.0} kPa  Temp {s.TemperatureAverage - 273.15f:0.0} C";
        _mode.Text = $"Mode: {s.Mode}";
    }
}
