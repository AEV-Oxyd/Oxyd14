using Content.Client._Oxyd.UI;
using Content.Shared._Oxyd.Technomancer;
using Robust.Client.GameObjects;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Eris rcon.tmpl port — one row per SMES (charge bar + AUTO/OFF input,
/// ONLINE/OFFLINE output pills) and one per breaker box (ENABLE/DISABLE).
/// </summary>
public sealed class OxydRconBui : BoundUserInterface
{
    private OxydRconWindow? _window;

    public OxydRconBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydRconWindow>();
        _window.Toggled += (ent, field, val) => SendMessage(new OxydRconToggleMessage { Entity = ent, Field = field, Value = val });
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydRconUiState s)
            _window?.Update(s);
    }
}

public sealed class OxydRconWindow : FancyWindow
{
    public event Action<NetEntity, string, bool>? Toggled;
    private readonly BoxContainer _rows;

    public OxydRconWindow()
    {
        Title = "RCON Remote Control Console";
        SetSize = new System.Numerics.Vector2(470, 430);
        var body = new ErisWindowBody();
        ContentsContainer.AddChild(body);
        var scroll = new ScrollContainer();
        body.AddChild(scroll);
        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        scroll.AddChild(_rows);
    }

    public void Update(OxydRconUiState state)
    {
        _rows.RemoveAllChildren();
        int i = 0;
        foreach (var row in state.Rows)
        {
            var line = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            var name = new Label { Text = $"{row.Group}  {row.Name}", HorizontalExpand = true };
            name.FontColorOverride = ErisNanoColors.ItemLabelText;
            line.AddChild(name);
            if (row.Group == "SMES")
            {
                var charge = new Label { Text = $" {row.ChargePercent}% " };
                line.AddChild(charge);
                var inp = new ErisLink(row.InputEnabled ? "IN:AUTO" : "IN:OFF");
                var inpOn = row.InputEnabled;
                if (inpOn) inp.SetSelectedLook();
                inp.OnPressed += _ => Toggled?.Invoke(row.Entity, "input", !inpOn);
                line.AddChild(inp);
                var outp = new ErisLink(row.OutputEnabled ? "OUT:ON" : "OUT:OFF");
                var outOn = row.OutputEnabled;
                if (outOn) outp.SetSelectedLook();
                outp.OnPressed += _ => Toggled?.Invoke(row.Entity, "output", !outOn);
                line.AddChild(outp);
            }
            else
            {
                var tog = new ErisLink(row.InputEnabled ? "ENABLED" : "DISABLED", !row.InputEnabled);
                var on = row.InputEnabled;
                if (on) tog.SetSelectedLook();
                tog.OnPressed += _ => Toggled?.Invoke(row.Entity, "breaker", !on);
                line.AddChild(tog);
            }
            _rows.AddChild(ErisCandystripe.Wrap(line, i++));
        }
    }
}
