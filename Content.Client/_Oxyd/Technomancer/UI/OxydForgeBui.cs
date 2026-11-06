using Content.Client._Oxyd.UI;
using Content.Shared._Oxyd.Technomancer;
using Robust.Client.GameObjects;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.Technomancer.UI;

/// <summary>
/// Eris nanoforge_actions.tmpl Adapt — matter bar + insert hint + eject pill
/// (autolathe.dm nanoforge; no recipes, no upgrades — pure matter compression).
/// </summary>
public sealed class OxydForgeBui : BoundUserInterface
{
    private OxydForgeWindow? _window;

    public OxydForgeBui(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydForgeWindow>();
        _window.Ejected += () => SendMessage(new OxydForgeEjectMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydForgeUiState s)
            _window?.Update(s);
    }
}

public sealed class OxydForgeWindow : FancyWindow
{
    public event Action? Ejected;
    private readonly ErisBar _bar;

    public OxydForgeWindow()
    {
        Title = "Nanoforge";
        SetSize = new System.Numerics.Vector2(400, 220);
        var body = new ErisWindowBody();
        ContentsContainer.AddChild(body);
        var col = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        body.AddChild(col);
        col.AddChild(new Label { Text = "Insert items to compress into matter" });
        _bar = new ErisBar();
        col.AddChild(_bar);
        var eject = new ErisLink("Eject compressed matter");
        eject.OnPressed += _ => Ejected?.Invoke();
        col.AddChild(eject);
    }

    public void Update(OxydForgeUiState s)
    {
        _bar.SetValue(s.Capacity > 0 ? (float)s.StoredMatter / s.Capacity : 0f,
            ErisNanoColors.BarGood, $"{s.StoredMatter}/{s.Capacity} u");
    }
}
