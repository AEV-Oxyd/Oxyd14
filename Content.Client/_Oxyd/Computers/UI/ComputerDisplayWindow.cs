using System.Numerics;
using Content.Client._Oxyd.Computers.Programs;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.StateRouting;
using Content.Client.UserInterface.Systems.Viewport;
using Content.Shared._Oxyd.Computers;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Primary visual frame / bezel for an NTOS computer entity.
/// Shows BIOS POST failure diagnostics if not booted, or yields the full screen surface
/// to the computer's Boot Program Entity once booted.
/// Automatically forces focus mode (docking over <c>ViewportContainer</c> and pausing world rendering)
/// only when actively viewing the surveillance camera app with more than 2 cameras active.
/// </summary>
public sealed class ComputerDisplayWindow : BaseWindow, IComputerDisplayHost
{
    public event Action? OnRetryBootPressed;

    public Control ScreenInsetControl { get; }
    public PipScreenSurface ScreenSurface { get; }
    public bool SupportsViewportDock => _supportsViewportDock;
    public bool IsDockedInViewport => _isDockedInViewport;
    public bool IsCameraFocusForced => _cameraFocusForced;
    public Vector2 PreferredFloatSize => _preferredFloatSize;

    private readonly ContentBoxContainer _bootErrorContainer;
    private readonly ContentLabel _titleLabel;
    private readonly ContentLabel _postHeaderLabel;
    private readonly ContentLabel _postDetailsLabel;
    private readonly ContentLabel _hwStatusLabel;
    private readonly ContentBoxContainer _bootProgramViewport;
    private readonly ContentPanelContainer _bezelPanel;
    private readonly StyleBoxFlat _bezelStyle;
    private readonly StyleBoxFlat _screenBgStyle;
    private bool _appliedInitialScreenSize;
    private float _autoExpandedHeight;
    private float _lastAppliedHeight;
    private bool _supportsViewportDock;
    private bool _cameraFocusForced;
    private bool _isDockedInViewport;
    private Vector2 _preferredFloatMinSize = new(560f, 440f);
    private Vector2 _preferredFloatSize = new(640f, 500f);

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
            SeparationOverride = 4,
            MouseFilter = MouseFilterMode.Ignore
        };

        _titleLabel = new ContentLabel
        {
            Text = "NTOS Display",
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
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
        if (_isDockedInViewport)
            return DragMode.None;

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

    public void SetSupportsViewportDock(bool supports)
    {
        _supportsViewportDock = supports;
        UpdateDockState();
    }

    public void SetCameraFocusForced(bool forced)
    {
        if (_cameraFocusForced == forced)
            return;

        _cameraFocusForced = forced;
        UpdateDockState();
    }

    public void UpdateDisplayState(ComputerDisplayUiState state)
    {
        _titleLabel.Text = $"{state.Title}{state.Subtitle} [{state.DeviceGuid}]";
        ScreenSurface.DeviceGuid = state.DeviceGuid;
        _supportsViewportDock = state.DockInViewport;

        if (state.ScreenSize.X >= 360 && state.ScreenSize.Y >= 320)
        {
            var rootSize = UserInterfaceManager.RootControl.Size;
            var targetW = rootSize.X > 360f ? MathF.Min(state.ScreenSize.X, rootSize.X - 16f) : state.ScreenSize.X;
            var targetH = rootSize.Y > 320f ? MathF.Min(state.ScreenSize.Y, rootSize.Y - 24f) : state.ScreenSize.Y;

            // Keep smaller subsystem controllers compact while letting large workstations & monitors open big
            // yet remain comfortably resizable down to 680x500 when floating (fits 1366x768 & 1280x720 screens).
            var minW = MathF.Min(targetW, 680f);
            var minH = MathF.Min(targetH, 500f);
            _preferredFloatMinSize = new Vector2(MathF.Max(360f, minW), MathF.Max(320f, minH));

            if (!_appliedInitialScreenSize)
            {
                var initW = MathF.Max(_preferredFloatMinSize.X, targetW);
                var initH = ClampAutoHeight(MathF.Max(MathF.Max(_preferredFloatMinSize.Y, targetH), _autoExpandedHeight), _preferredFloatMinSize.Y);
                _preferredFloatSize = new Vector2(initW, initH);
                _lastAppliedHeight = initH;
                _appliedInitialScreenSize = true;

                if (!ShouldBeDockedNow())
                {
                    MinSize = _preferredFloatMinSize;
                    SetSize = _preferredFloatSize;
                    CenterInsideScreen();
                }
            }
            else if (!_isDockedInViewport)
            {
                MinSize = _preferredFloatMinSize;
                if (!float.IsNaN(SetWidth) && SetWidth < MinSize.X)
                {
                    SetSize = new Vector2(MinSize.X, MathF.Max(SetHeight, MinSize.Y));
                    KeepInsideScreen();
                }
            }
        }

        RefreshCameraFocusState();
        UpdateDockState();

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
                $"RAM: {(state.HasRam ? $"{state.RamTotalMb} QB" : "MISSING")}\n" +
                $"HDD: {(state.HasHardDrive ? Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(state.StorageTotalBytes) : "MISSING")}\n" +
                $"NIC: {(state.HasNetworkCard ? "PRESENT" : "NONE")} | DATA PORT: {(state.HasDataPort ? "PRESENT" : "NONE")}";
        }

        ScreenSurface.InvalidateCapture();
        PipSurfaceRegistry.InvalidateDevice(ScreenSurface.DeviceGuid);
    }

    /// <summary>
    /// Allows an embedded program UI (such as the Autolathe controller expanding for up to 8 recipes)
    /// to resize the window height up to the available viewport height, unless the user manually resized it.
    /// </summary>
    public void RequestAutoExpandedHeight(float preferredHeight)
    {
        _autoExpandedHeight = preferredHeight;

        // Wait until UpdateDisplayState applies the device's ScreenSize if it hasn't yet,
        // otherwise apply immediately (unless the user has manually resized the window).
        if (!_appliedInitialScreenSize)
            return;

        var minY = _preferredFloatMinSize.Y;
        var targetHeight = ClampAutoHeight(MathF.Max(minY, preferredHeight), minY);
        _preferredFloatSize = new Vector2(MathF.Max(_preferredFloatMinSize.X, _preferredFloatSize.X), targetHeight);

        if (_isDockedInViewport)
            return;

        if (!float.IsNaN(SetHeight) &&
            _lastAppliedHeight > 0f &&
            MathF.Abs(SetHeight - _lastAppliedHeight) > 2f)
        {
            return;
        }

        var width = MathF.Max(MinSize.X, !float.IsNaN(SetWidth) ? SetWidth : 640f);
        SetSize = new Vector2(width, targetHeight);
        _lastAppliedHeight = targetHeight;
        KeepInsideScreen();
    }

    private float ClampAutoHeight(float desiredHeight, float minHeight)
    {
        var minH = MathF.Max(minHeight, 320f);
        var clamped = MathF.Max(minH, desiredHeight);
        var rootHeight = UserInterfaceManager.RootControl.Size.Y;
        if (rootHeight > 320f)
            clamped = MathF.Min(clamped, MathF.Max(minH, rootHeight - 24f));
        return clamped;
    }

    public void RefreshCameraFocusState()
    {
        var camUi = FindActiveCameraControl(_bootProgramViewport);
        var shouldForce = camUi is { ShouldForceViewportFocus: true };
        if (_cameraFocusForced != shouldForce)
            _cameraFocusForced = shouldForce;
    }

    private static SurveillanceCameraUiControl? FindActiveCameraControl(Control root)
    {
        if (root.Disposed || !root.Visible)
            return null;

        if (root is SurveillanceCameraUiControl cam)
            return cam;

        for (var i = 0; i < root.ChildCount; i++)
        {
            var found = FindActiveCameraControl(root.GetChild(i));
            if (found != null)
                return found;
        }

        return null;
    }

    private bool ShouldBeDockedNow()
    {
        if (!_supportsViewportDock || !_cameraFocusForced || Disposed || !Visible || !IsInsideTree)
            return false;

        if (Parent != UserInterfaceManager.WindowRoot)
            return false;

        return UserInterfaceManager.GetUIController<ViewportUIController>().TryGetViewportFocusBounds(out _);
    }

    private void UpdateDockState()
    {
        if (ShouldBeDockedNow())
        {
            if (!_isDockedInViewport)
            {
                _isDockedInViewport = true;
                Resizable = false;
                MinSize = Vector2.Zero;
                UserInterfaceManager.GetUIController<ViewportUIController>().SetWorldRenderPaused(this, true);
            }

            SyncDockedBounds();
        }
        else
        {
            ReleaseViewportDock(restoreFloatLayout: true);
        }
    }

    private void SyncDockedBounds()
    {
        if (!_isDockedInViewport || Parent == null)
            return;

        if (!UserInterfaceManager.GetUIController<ViewportUIController>().TryGetViewportFocusBounds(out var focusBounds))
            return;

        var localTopLeft = focusBounds.TopLeft - Parent.GlobalPosition;
        var targetSize = focusBounds.Size;

        if ((Position - localTopLeft).LengthSquared() > 0.25f)
            Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(this, localTopLeft);

        if (float.IsNaN(SetWidth) || float.IsNaN(SetHeight) ||
            MathF.Abs(SetWidth - targetSize.X) > 0.25f ||
            MathF.Abs(SetHeight - targetSize.Y) > 0.25f)
        {
            SetSize = targetSize;
        }
    }

    private void ReleaseViewportDock(bool restoreFloatLayout)
    {
        UserInterfaceManager.GetUIController<ViewportUIController>().SetWorldRenderPaused(this, false);

        if (!_isDockedInViewport)
            return;

        _isDockedInViewport = false;
        Resizable = true;

        if (restoreFloatLayout && !Disposed)
        {
            MinSize = _preferredFloatMinSize;
            SetSize = _preferredFloatSize;
            _lastAppliedHeight = _preferredFloatSize.Y;
            if (Parent == UserInterfaceManager.WindowRoot)
                CenterInsideScreen();
        }
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        RefreshCameraFocusState();
        UpdateDockState();
    }

    protected override void ExitedTree()
    {
        ReleaseViewportDock(restoreFloatLayout: true);
        base.ExitedTree();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_isDockedInViewport)
            SyncDockedBounds();
    }

    private void CenterInsideScreen()
    {
        if (Parent == null)
            return;

        var rootSize = UserInterfaceManager.RootControl.Size;
        if (rootSize.X <= 360f || rootSize.Y <= 320f)
            return;

        var w = !float.IsNaN(SetWidth) ? SetWidth : MathF.Max(Size.X, MinSize.X);
        var h = !float.IsNaN(SetHeight) ? SetHeight : MathF.Max(Size.Y, MinSize.Y);
        var maxX = MathF.Max(8f, rootSize.X - w - 8f);
        var maxY = MathF.Max(8f, rootSize.Y - h - 8f);
        var centered = new Vector2(
            Math.Clamp((rootSize.X - w) * 0.5f, 8f, maxX),
            Math.Clamp((rootSize.Y - h) * 0.5f, 8f, maxY));

        Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(this, centered);
    }

    private void KeepInsideScreen()
    {
        if (Parent == null || Size.X <= 0f || Size.Y <= 0f)
            return;

        var rootSize = UserInterfaceManager.RootControl.Size;
        if (rootSize.X <= 360f || rootSize.Y <= 320f)
            return;

        var w = !float.IsNaN(SetWidth) ? SetWidth : MathF.Max(Size.X, MinSize.X);
        var h = !float.IsNaN(SetHeight) ? SetHeight : MathF.Max(Size.Y, MinSize.Y);
        var pos = Position;
        var maxX = MathF.Max(8f, rootSize.X - w - 8f);
        var maxY = MathF.Max(8f, rootSize.Y - h - 8f);
        var clampedPos = new Vector2(
            Math.Clamp(pos.X, 8f, maxX),
            Math.Clamp(pos.Y, 8f, maxY));

        if ((clampedPos - pos).LengthSquared() > 1f)
            Robust.Client.UserInterface.Controls.LayoutContainer.SetPosition(this, clampedPos);
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

        ReleaseViewportDock(restoreFloatLayout: false);
        base.Close();
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseViewportDock(restoreFloatLayout: false);
            UnmountAllBootPrograms();
        }

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
        if (EntMan.TryGetComponent<OxydComputerComponent>(Owner, out var comp))
            _window.SetSupportsViewportDock(comp.DockInViewport);
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
