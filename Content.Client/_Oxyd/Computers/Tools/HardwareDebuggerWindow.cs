using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.StateRouting;
using Content.Shared._Oxyd.Computers.Tools;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Tools;

public sealed class HardwareDebuggerWindow : DefaultWindow
{
    public event Action<string>? OnExecuteCommand;
    public event Action? OnDisconnectPressed;

    private readonly ContentLabel _targetLabel;
    private readonly ContentLabel _hwLabel;
    private readonly ContentBoxContainer _portsContainer;
    private readonly ContentScrollContainer _scroll;
    private readonly ContentBoxContainer _historyContainer;
    private readonly ContentLabel _promptLabel;
    private readonly ContentLineEdit _input;

    public HardwareDebuggerWindow()
    {
        Title = "NT Hardware Debugging Terminal [DATA PORT]";
        MinSize = new Vector2(620, 480);

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        var topRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _targetLabel = new ContentLabel { Text = "Disconnected", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var discBtn = new ContentButton { Text = "Unplug", MinWidth = 70 };
        discBtn.OnPressed += _ => OnDisconnectPressed?.Invoke();
        topRow.AddChild(_targetLabel);
        topRow.AddChild(discBtn);
        root.AddChild(topRow);

        _hwLabel = new ContentLabel { Text = "", FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_hwLabel);

        _portsContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_portsContainer);

        _scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _historyContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        _scroll.AddChild(_historyContainer);
        root.AddChild(_scroll);

        var inRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        _promptLabel = new ContentLabel { Text = "jtag> ", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 0, 4, 0) };
        _input = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Enter data port command (hw, devlink list, ls, boot)..." };
        _input.OnTextEntered += args =>
        {
            if (string.IsNullOrWhiteSpace(args.Text))
                return;
            var cmd = args.Text;
            _input.Text = string.Empty;
            OnExecuteCommand?.Invoke(cmd);
        };
        inRow.AddChild(_promptLabel);
        inRow.AddChild(_input);
        root.AddChild(inRow);

        ContentsContainer.AddChild(root);
    }

    public void UpdateState(HardwareDebuggerUiState state)
    {
        _targetLabel.Text = state.IsConnected
            ? $"Connected: {state.HostMachineName} -> {state.TargetName} [{state.TargetGuid}] via {state.DataPortLabel}"
            : "Disconnected";
        _hwLabel.Text = $"{state.HardwareSummary} | POST: {(state.IsBooted ? "BOOTED" : state.BootError)}";

        _portsContainer.RemoveAllChildren();
        foreach (var port in state.Ports)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new ContentLabel
            {
                Text = $"[{(port.IsHostPort ? "HOST" : "COMP")} {(port.IsSource ? "OUT" : "IN ")}] {port.PortId} ({port.PortName})",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#AAAAAA")
            });
            var trig = new ContentButton { Text = "Trigger", MinWidth = 65 };
            trig.OnPressed += _ => OnExecuteCommand?.Invoke($"devlink trigger {port.PortId}");
            row.AddChild(trig);
            _portsContainer.AddChild(row);
        }

        _promptLabel.Text = state.Prompt;
        _historyContainer.RemoveAllChildren();
        foreach (var line in state.Lines)
        {
            _historyContainer.AddChild(new ContentLabel { Text = line, FontColorOverride = Color.FromHex("#D6F5F5") });
        }
        _scroll.ScrollOffsetY = float.MaxValue;
    }
}

public sealed class HardwareDebuggerBoundUserInterface : RoutedBoundUserInterface
{
    private HardwareDebuggerWindow? _window;

    public HardwareDebuggerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HardwareDebuggerWindow>();
        SetStateScopeRoot(_window);
        _window.OnExecuteCommand += cmd => SendMessage(new HardwareDebuggerCommandMessage(cmd));
        _window.OnDisconnectPressed += () => SendMessage(new HardwareDebuggerDisconnectMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HardwareDebuggerUiState dbgState)
            _window?.UpdateState(dbgState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            SetStateScopeRoot(null);
    }
}
