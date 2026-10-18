using Content.Client._Oxyd.UI;
using Content.Shared._Oxyd.Technomancer;
using Robust.Client.GameObjects;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Eris atmos_control.tmpl Adapt — fixed console listing every air alarm on
/// the map with location + alarm status (modular program covered as fixed
/// console per blueprint §7/§11).
/// </summary>
public sealed class OxydAtmosBui : BoundUserInterface
{
    private OxydAtmosWindow? _window;

    public OxydAtmosBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydAtmosWindow>();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydAtmosUiState s)
            _window?.Update(s);
    }
}

public sealed class OxydAtmosWindow : FancyWindow
{
    private readonly BoxContainer _rows;

    public OxydAtmosWindow()
    {
        Title = "Atmospherics Control";
        SetSize = new System.Numerics.Vector2(440, 380);
        var body = new ErisWindowBody();
        ContentsContainer.AddChild(body);
        var scroll = new ScrollContainer();
        body.AddChild(scroll);
        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        scroll.AddChild(_rows);
    }

    public void Update(OxydAtmosUiState state)
    {
        _rows.RemoveAllChildren();
        int i = 0;
        foreach (var row in state.Rows)
        {
            var line = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            var name = new Label { Text = row.Name, HorizontalExpand = true };
            name.FontColorOverride = ErisNanoColors.ItemLabelText;
            line.AddChild(name);
            var area = new Label { Text = row.Area + "  " };
            line.AddChild(area);
            var status = new Label { Text = row.Status };
            status.FontColorOverride = row.Status == "Normal" ? ErisNanoColors.BarGood : ErisNanoColors.BarBad;
            line.AddChild(status);
            _rows.AddChild(ErisCandystripe.Wrap(line, i++));
        }
    }
}
