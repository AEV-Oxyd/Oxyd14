using System.Numerics;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.StateRouting;
using Content.Shared._Oxyd.Computers;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
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

    private readonly ContentBoxContainer _bootErrorContainer;
    private readonly ContentLabel _titleLabel;
    private readonly ContentLabel _postHeaderLabel;
    private readonly ContentLabel _postDetailsLabel;
    private readonly ContentLabel _hwStatusLabel;
    private readonly ContentBoxContainer _bootProgramViewport;
    private readonly ContentPanelContainer _bezelPanel;
    private readonly StyleBoxFlat _bezelStyle;
    private readonly StyleBoxFlat _screenBgStyle;

    public ComputerDisplayWindow()
    {
        MouseFilter = MouseFilterMode.Stop;
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

        _bezelPanel = new ContentPanelContainer
        {
            PanelOverride = _bezelStyle,
            HorizontalExpand = true,
            VerticalExpand = true,
            MouseFilter = MouseFilterMode.Ignore
        };

        var rootBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            MouseFilter = MouseFilterMode.Ignore
        };

        // Top window drag header
        var headerBar = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(4, 2, 4, 4),
            MouseFilter = MouseFilterMode.Ignore
        };

        _titleLabel = new ContentLabel
        {
            Text = "NTOS Display",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            MouseFilter = MouseFilterMode.Ignore
        };

        var closeBtn = new ContentButton
        {
            Name = "CloseButton",
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

        ScreenInsetControl = new ContentPanelContainer
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

        _bootErrorContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(18),
            Visible = true
        };

        _postHeaderLabel = new ContentLabel
        {
            Text = "NTOS BIOS / HARDWARE POST",
            FontColorOverride = Color.FromHex("#FF5555"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _postDetailsLabel = new ContentLabel
        {
            Text = "Waiting for system boot...",
            FontColorOverride = Color.FromHex("#E0F7FA"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _hwStatusLabel = new ContentLabel
        {
            Text = "",
            FontColorOverride = Color.FromHex("#88AAAA"),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var retryBtn = new ContentButton
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
        const int resizeMargin = 6;
        var mode = DragMode.None;

        if (Resizable)
        {
            if (relativeMousePos.Y < 3)
                mode |= DragMode.Top;
            else if (relativeMousePos.Y > Size.Y - resizeMargin)
                mode |= DragMode.Bottom;

            if (relativeMousePos.X < resizeMargin)
                mode |= DragMode.Left;
            else if (relativeMousePos.X > Size.X - resizeMargin)
                mode |= DragMode.Right;
        }

        if (mode == DragMode.None && relativeMousePos.Y < 42)
            mode = DragMode.Move;

        return mode;
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
        PipSurfaceRegistry.InvalidateDevice(ScreenSurface.DeviceGuid);
    }

    public void MountBootProgram(ProgramUiControl control)
    {
        if (Disposed || control.Disposed || _bootProgramViewport.Disposed)
            return;

        if (control.Parent == _bootProgramViewport && _bootProgramViewport.ChildCount == 1)
            return;

        control.Parent?.RemoveChild(control);
        // Detach without disposing any previous child control since ProgramBoundUserInterface owns its lifetime.
        _bootProgramViewport.RemoveAllChildren();
        control.HorizontalExpand = true;
        control.VerticalExpand = true;
        _bootProgramViewport.AddChild(control);
        ScreenSurface.InvalidateCapture();
        PipSurfaceRegistry.InvalidateDevice(ScreenSurface.DeviceGuid);
    }

    public void UnmountBootProgram(ProgramUiControl control)
    {
        if (_bootProgramViewport.Disposed)
            return;

        if (control.Parent == _bootProgramViewport)
            _bootProgramViewport.RemoveChild(control);
        ScreenSurface.InvalidateCapture();
        PipSurfaceRegistry.InvalidateDevice(ScreenSurface.DeviceGuid);
    }

    public void UnmountAllBootPrograms()
    {
        if (!_bootProgramViewport.Disposed)
            _bootProgramViewport.RemoveAllChildren();
    }

    public override void Close()
    {
        if (ScreenSharing.RemoteScreenView.RemoteBuiTarget.TryHandleWindowClose(this))
            return;

        base.Close();
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            UnmountAllBootPrograms();

        base.Dispose(disposing);
    }
}

public sealed class ComputerBoundUserInterface : RoutedBoundUserInterface
{
    private ComputerDisplayWindow? _window;

    public ComputerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        var wasOpened = IsOpened;
        base.Open();

        if (wasOpened && _window != null && !_window.Disposed)
        {
            SetStateScopeRoot(_window);
            EntMan.System<ClientComputerUiSystem>().RegisterDisplayHost(Owner, _window);
            return;
        }

        if (_window != null)
        {
            EntMan.System<ClientComputerUiSystem>().UnregisterDisplayHost(Owner, _window);
            _window.UnmountAllBootPrograms();
            SetStateScopeRoot(null);
            if (!_window.Disposed)
                _window.Dispose();
            _window = null;
        }

        _window = this.CreateWindow<ComputerDisplayWindow>();
        SetStateScopeRoot(_window);
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
        if (disposing && _window != null)
        {
            EntMan.System<ClientComputerUiSystem>().UnregisterDisplayHost(Owner, _window);
            _window.UnmountAllBootPrograms();
            SetStateScopeRoot(null);
            _window = null;
        }

        base.Dispose(disposing);
    }
}
