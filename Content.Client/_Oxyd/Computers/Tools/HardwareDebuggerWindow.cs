using System.Numerics;
using Content.Shared._Oxyd.Computers.Tools;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Tools;

public sealed class HardwareDebuggerWindow : DefaultWindow
{
    public event Action<string>? OnExecuteCommand;
    public event Action? OnDisconnectPressed;

    private readonly Label _targetLabel;
    private readonly Label _hwLabel;
    private readonly BoxContainer _portsContainer;
    private readonly ScrollContainer _scroll;
    private readonly BoxContainer _historyContainer;
    private readonly Label _promptLabel;
    private readonly LineEdit _input;

    public HardwareDebuggerWindow()
    {
        Title = "NT Hardware Debugging Terminal [DATA PORT]";
        MinSize = new Vector2(620, 480);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        var topRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _targetLabel = new Label { Text = "Disconnected", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var discBtn = new Button { Text = "Unplug", MinWidth = 70 };
        discBtn.OnPressed += _ => OnDisconnectPressed?.Invoke();
        topRow.AddChild(_targetLabel);
        topRow.AddChild(discBtn);
        root.AddChild(topRow);

        _hwLabel = new Label { Text = "", FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_hwLabel);

        _portsContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_portsContainer);

        _scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _historyContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        _scroll.AddChild(_historyContainer);
        root.AddChild(_scroll);

        var inRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        _promptLabel = new Label { Text = "jtag> ", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 0, 4, 0) };
        _input = new LineEdit { HorizontalExpand = true, PlaceHolder = "Enter data port command (hw, devlink list, ls, boot)..." };
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
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label
            {
                Text = $"[{(port.IsHostPort ? "HOST" : "COMP")} {(port.IsSource ? "OUT" : "IN ")}] {port.PortId} ({port.PortName})",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#AAAAAA")
            });
            var trig = new Button { Text = "Trigger", MinWidth = 65 };
            trig.OnPressed += _ => OnExecuteCommand?.Invoke($"devlink trigger {port.PortId}");
            row.AddChild(trig);
            _portsContainer.AddChild(row);
        }

        _promptLabel.Text = state.Prompt;
        _historyContainer.RemoveAllChildren();
        foreach (var line in state.Lines)
        {
            _historyContainer.AddChild(new Label { Text = line, FontColorOverride = Color.FromHex("#D6F5F5") });
        }
        _scroll.SetScrollValue(new Vector2(0, float.MaxValue));
    }
}

public sealed class HardwareDebuggerBoundUserInterface : BoundUserInterface
{
    private HardwareDebuggerWindow? _window;

    public HardwareDebuggerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HardwareDebuggerWindow>();
        _window.OnExecuteCommand += cmd => SendMessage(new HardwareDebuggerCommandMessage(cmd));
        _window.OnDisconnectPressed += () => SendMessage(new HardwareDebuggerDisconnectMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HardwareDebuggerUiState dbgState)
            _window?.UpdateState(dbgState);
    }
}
