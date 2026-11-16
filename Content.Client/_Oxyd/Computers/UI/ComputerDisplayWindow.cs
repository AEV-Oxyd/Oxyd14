using System.Numerics;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Primary visual frame / bezel for an NTOS computer entity.
/// Shows BIOS POST failure diagnostics if not booted, or yields the full screen surface
/// to the computer's Boot Program Entity once booted.
/// </summary>
public sealed class ComputerDisplayWindow : BaseWindow, IComputerDisplayHost
{
    public event Action? OnRetryBootPressed;

    public Control ScreenInsetControl { get; }
    public PipScreenSurface ScreenSurface { get; }

    private readonly BoxContainer _bootErrorContainer;
    private readonly Label _titleLabel;
    private readonly Label _postHeaderLabel;
    private readonly Label _postDetailsLabel;
    private readonly Label _hwStatusLabel;
    private readonly ContentBoxContainer _bootProgramViewport;
    private readonly PanelContainer _bezelPanel;
    private readonly StyleBoxFlat _bezelStyle;
    private readonly StyleBoxFlat _screenBgStyle;

    public ComputerDisplayWindow()
    {
        MinSize = new Vector2(560, 440);
        SetSize = new Vector2(640, 500);

        _bezelStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#11151C"),
            BorderColor = Color.FromHex("#00D4FF"),
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 6,
            ContentMarginRightOverride = 6,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 6
        };

        _screenBgStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#080D14"),
            BorderColor = Color.FromHex("#005577"),
            BorderThickness = new Thickness(1)
        };

        _bezelPanel = new PanelContainer
        {
            PanelOverride = _bezelStyle,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        var rootBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        // Top window drag header
        var headerBar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(4, 2, 4, 4)
        };

        _titleLabel = new Label
        {
            Text = "NTOS Display",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };

        var closeBtn = new Button
        {
            Text = "X",
            SetWidth = 28
        };
        closeBtn.OnPressed += _ => Close();

        headerBar.AddChild(_titleLabel);
        headerBar.AddChild(closeBtn);
        rootBox.AddChild(headerBar);

        // Screen surface for both local display & remote screen PiP capture
        ScreenSurface = new PipScreenSurface
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };

        ScreenInsetControl = new PanelContainer
        {
            PanelOverride = _screenBgStyle,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true
        };

        _bootProgramViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };

        _bootErrorContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(18),
            Visible = true
        };

        _postHeaderLabel = new Label
        {
            Text = "NTOS BIOS / HARDWARE POST",
            FontColorOverride = Color.FromHex("#FF5555"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _postDetailsLabel = new Label
        {
            Text = "Waiting for system boot...",
            FontColorOverride = Color.FromHex("#E0F7FA"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _hwStatusLabel = new Label
        {
            Text = "",
            FontColorOverride = Color.FromHex("#88AAAA"),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var retryBtn = new ContentStandardButton
        {
            Text = "Retry Hardware POST / Boot",
            HorizontalAlignment = HAlignment.Left,
            SetWidth = 220
        };
        retryBtn.OnPressed += _ => OnRetryBootPressed?.Invoke();

        _bootErrorContainer.AddChild(_postHeaderLabel);
        _bootErrorContainer.AddChild(_postDetailsLabel);
        _bootErrorContainer.AddChild(_hwStatusLabel);
        _bootErrorContainer.AddChild(retryBtn);

        ScreenInsetControl.AddChild(_bootProgramViewport);
        ScreenInsetControl.AddChild(_bootErrorContainer);
        ScreenSurface.AddChild(ScreenInsetControl);
        rootBox.AddChild(ScreenSurface);
        _bezelPanel.AddChild(rootBox);
        AddChild(_bezelPanel);
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        return relativeMousePos.Y < 32 ? DragMode.Move : DragMode.None;
    }

    public void UpdateDisplayState(ComputerDisplayUiState state)
    {
        _titleLabel.Text = $"{state.Title}{state.Subtitle} [{state.DeviceGuid}]";
        ScreenSurface.DeviceGuid = state.DeviceGuid;
        if (state.ScreenSize.X >= 360 && state.ScreenSize.Y >= 320)
            MinSize = new Vector2(state.ScreenSize.X, state.ScreenSize.Y);

        if (Color.TryFromHex(state.BorderColor, out var borderCol))
        {
            _bezelStyle.BorderColor = borderCol;
            _titleLabel.FontColorOverride = borderCol;
        }

        _bootProgramViewport.Visible = state.IsBooted;
        _bootErrorContainer.Visible = !state.IsBooted;

        if (!state.IsBooted)
        {
            _postDetailsLabel.Text = string.IsNullOrWhiteSpace(state.BootError)
                ? "Boot sequence halted."
                : state.BootError;

            _hwStatusLabel.Text =
                $"CPU: {(state.HasCpu ? $"{state.CpuFrequencyMhz} MHz" : "MISSING")}\n" +
                $"RAM: {(state.HasRam ? $"{state.RamTotalMb} MB" : "MISSING")}\n" +
                $"HDD: {(state.HasHardDrive ? $"{state.StorageTotalBytes / 1024} KB" : "MISSING")}\n" +
                $"NIC: {(state.HasNetworkCard ? "PRESENT" : "NONE")} | DATA PORT: {(state.HasDataPort ? "PRESENT" : "NONE")}";
        }

        ScreenSurface.InvalidateCapture();
    }

    public void MountBootProgram(ProgramUiControl control)
    {
        if (control.Parent == _bootProgramViewport)
            return;

        control.Parent?.RemoveChild(control);
        _bootProgramViewport.RemoveAllChildren();
        control.HorizontalExpand = true;
        control.VerticalExpand = true;
        _bootProgramViewport.AddChild(control);
        ScreenSurface.InvalidateCapture();
    }

    public void UnmountBootProgram(ProgramUiControl control)
    {
        if (control.Parent == _bootProgramViewport)
            _bootProgramViewport.RemoveChild(control);
        ScreenSurface.InvalidateCapture();
    }
}

public sealed class ComputerBoundUserInterface : BoundUserInterface
{
    private ComputerDisplayWindow? _window;

    public ComputerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ComputerDisplayWindow>();
        _window.OnRetryBootPressed += () => SendMessage(new ComputerRetryBootMessage());
        EntMan.System<ClientComputerUiSystem>().RegisterDisplayHost(Owner, _window);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ComputerDisplayUiState compState)
            _window?.UpdateDisplayState(compState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _window != null)
            EntMan.System<ClientComputerUiSystem>().UnregisterDisplayHost(Owner, _window);
    }
}
