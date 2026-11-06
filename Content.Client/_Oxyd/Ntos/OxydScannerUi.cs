using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydScannerUi : UIFragment
{
    private OxydScannerUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydScannerUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydScannerUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydScannerUiFragment : BoxContainer
{
    public event Action<OxydScannerUiMessageEvent>? OnMessage;

    private readonly Label _kind = new();
    private readonly Label _error = new() { Visible = false };
    private readonly Label _buffer = new();
    private readonly Button _print = new() { Text = "Print" };

    public OxydScannerUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 240;

        AddChild(_kind);
        AddChild(_error);
        AddChild(new Label
        {
            Text = "Medical/atmos: use PDA on target. Price/reagent: click target to scan into buffer.",
        });
        AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            MinHeight = 120,
            Children = { _buffer },
        });

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var refresh = new Button { Text = "Refresh" };
        refresh.OnPressed += _ => OnMessage?.Invoke(new OxydScannerUiMessageEvent(OxydScannerUiAction.Refresh));
        var clear = new Button { Text = "Clear" };
        clear.OnPressed += _ => OnMessage?.Invoke(new OxydScannerUiMessageEvent(OxydScannerUiAction.Clear));
        _print.OnPressed += _ => OnMessage?.Invoke(new OxydScannerUiMessageEvent(OxydScannerUiAction.Print));
        row.AddChild(refresh);
        row.AddChild(clear);
        row.AddChild(_print);
        AddChild(row);
    }

    public void UpdateState(OxydScannerUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _kind.Text = state.Status ?? $"Scanner module: {state.ScannerKind}";
        _buffer.Text = string.IsNullOrWhiteSpace(state.Buffer) ? "(empty buffer)" : state.Buffer;
        _print.Disabled = !state.CanPrint;
    }
}
