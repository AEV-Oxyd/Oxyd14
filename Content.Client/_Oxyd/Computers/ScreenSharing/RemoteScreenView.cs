using System.Numerics;
using System.Reflection;
using Content.Client.PDA;
using Content.Client.Power.APC.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.StateRouting;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.APC;
using Content.Shared.PDA;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Universal remote screen container capable of generic PIP-rendering of any target UI:
/// - BoundUserInterface devices (APCs, Atmospheric Alarms, Static Computers, Consoles, PDAs, etc.)
/// - Arbitrary custom IRemoteTargetUi implementations
/// Fits the remote display cleanly to the terminal viewport and streams user interactions
/// (mouse clicks, hovers, scroll, keyboard typing, focus) directly to the target UI.
/// Infinite recursive cycles (A -> B -> A or A -> A) are rendered via Picture-in-Picture (PiP) mirror textures.
/// </summary>
public sealed partial class RemoteScreenView : LayoutContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private static readonly MethodInfo? s_ControlKeyBindDown =
        typeof(Control).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly MethodInfo? s_ControlKeyBindUp =
        typeof(Control).GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly MethodInfo? s_ControlMouseMove =
        typeof(Control).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly MethodInfo? s_ControlMouseWheel =
        typeof(Control).GetMethod("MouseWheel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo? s_ButtonAttemptingPress =
        typeof(BaseButton).GetField("_attemptingPress", BindingFlags.Instance | BindingFlags.NonPublic);

    public BoundUserInterface? HostBui
    {
        get => _hostBui;
        set
        {
            _hostBui = value;
            _activeTargetUi?.SetHostBui(value);
        }
    }
    private BoundUserInterface? _hostBui;

    public event Action<string?, string?>? OnRemoteAction;

    private readonly PipScreenSurface _surface;
    private readonly Label _statusLabel;

    private IRemoteTargetUi? _activeTargetUi;
    private string? _deviceGuid;
    private bool _isCycle;

    private readonly Dictionary<BoundKeyFunction, Control> _relayedPressedControls = new();

    private Control? GetCaptureTarget()
    {
        
        if (_activeTargetUi?.RootControl is not { } root || root.Disposed)
            return null;

        if (root is PdaWindow pdaWin)
            return pdaWin.ScreenInsetControl;

        if (root is FancyWindow fancy)
            return fancy.ContentsContainer;

        return root;
    }

    private Vector2 TargetResolution
    {
        get
        {
            if (GetCaptureTarget() is { } target)
            {
                if (target.PixelSize.X > 0 && target.PixelSize.Y > 0)
                    return target.Size;
                if (target.SetSize.X > 0 && target.SetSize.Y > 0)
                    return target.SetSize;
                if (target.MinSize.X > 0 && target.MinSize.Y > 0)
                    return target.MinSize;
            }

            return _activeTargetUi?.TargetSize ?? new Vector2(640, 520);
        }
    }

    public RemoteScreenView()
    {
        IoCManager.InjectDependencies(this);

        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;

        _statusLabel = new Label
        {
            Text = "INITIALIZING REMOTE STREAM...",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Visible = true
        };
        AddChild(_statusLabel);
        LayoutContainer.SetAnchorAndMarginPreset(_statusLabel, LayoutPreset.Wide);

        _surface = new PipScreenSurface
        {
            MouseFilter = MouseFilterMode.Ignore
        };
        AddChild(_surface);
        LayoutContainer.SetAnchorAndMarginPreset(_surface, LayoutPreset.Wide);
    }

    public void UpdateState(RemoteScreenState state)
    {
        _deviceGuid = state.DeviceGuid;

        var isCycle = state.IsCycleReference ||
                      PipSurfaceRegistry.IsDeviceInAncestorChain(this, state.DeviceGuid);

        if (isCycle)
        {
            _isCycle = true;
            _surface.Visible = false;
            _surface.DeviceGuid = null;
            _statusLabel.Visible = false;

            if (!string.IsNullOrEmpty(_deviceGuid))
            {
                var surface = PipSurfaceRegistry.ResolveSurface(this, _deviceGuid);
                surface?.RequestCaptureFrame(4);
                surface?.InvalidateCapture(3);
            }
            return;
        }

        _isCycle = false;
        _surface.Visible = true;
        _surface.DeviceGuid = state.DeviceGuid;
        _surface.InvalidateCapture();
        PipSurfaceRegistry.FindAncestorSurface(this, state.ViewingTargetGuid)?.InvalidateCapture();

        // Universal BoundUserInterface Device (APC, Air Alarm, Static Computer, Consoles, PDAs, etc.)
        if (state.TargetEntity != NetEntity.Invalid)
        {
            var uiKey = state.UiKey ?? PdaUiKey.Key;
            if (_activeTargetUi is not RemoteBuiTarget buiTarget || !buiTarget.Matches(state.TargetEntity, uiKey))
            {
                SetTargetUi(new RemoteBuiTarget(state.TargetEntity, uiKey, state.DeviceName, _entManager, _playerManager, _uiManager));
            }

            _activeTargetUi?.UpdateState(state);
            return;
        }

        DisposeActiveTarget();
        _statusLabel.Text = $"STREAM CONNECTED: {state.DeviceName} (NO ACTIVE UI)";
        _statusLabel.Visible = true;
    }

    public void SetTargetUi(IRemoteTargetUi targetUi)
    {
        DisposeActiveTarget();
        _activeTargetUi = targetUi;
        _activeTargetUi.SetHostBui(_hostBui);

        _statusLabel.Visible = false;
        _surface.InvalidateCapture();
    }

    private void DisposeActiveTarget()
    {
        if (_activeTargetUi != null)
        {
            _activeTargetUi.Dispose();
            _activeTargetUi = null;
        }
        _relayedPressedControls.Clear();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_isCycle && !string.IsNullOrEmpty(_deviceGuid))
        {
            var surface = PipSurfaceRegistry.ResolveSurface(this, _deviceGuid);
            surface?.RequestCaptureFrame(4);
        }

        _activeTargetUi?.FrameUpdate(args);
    }

    protected override void RenderChildOverride(ref ControlRenderArguments args, int childIndex, Vector2i position)
    {
        var child = GetChild(childIndex);
        if (child == _surface)
            return;

        base.RenderChildOverride(ref args, childIndex, position);
    }

    private UIBox2 GetFittedScreenRect()
    {
        var targetPixelSize = (Vector2i)(TargetResolution * UIScale);
        if (targetPixelSize.X <= 0 || targetPixelSize.Y <= 0 || PixelWidth <= 0 || PixelHeight <= 0)
            return new UIBox2(Vector2.Zero, PixelSize);

        // Aspect ratio fit (preserves proportions, adds letterbox/pillarbox inside CRT frame)
        var scaleX = (float)PixelWidth / targetPixelSize.X;
        var scaleY = (float)PixelHeight / targetPixelSize.Y;
        var scale = MathF.Min(scaleX, scaleY);

        var fittedW = MathF.Round(targetPixelSize.X * scale);
        var fittedH = MathF.Round(targetPixelSize.Y * scale);

        var offsetX = MathF.Round((PixelWidth - fittedW) / 2f);
        var offsetY = MathF.Round((PixelHeight - fittedH) / 2f);

        return new UIBox2(offsetX, offsetY, offsetX + fittedW, offsetY + fittedH);
    }

    private bool TryMapToTarget(Vector2 localPixelPos, out Vector2 targetPixelPos)
    {
        targetPixelPos = Vector2.Zero;
        var destRect = GetFittedScreenRect();
        if (!destRect.Contains(localPixelPos) || destRect.Width <= 0 || destRect.Height <= 0)
            return false;

        var hostPixelSize = (Vector2i)(TargetResolution * UIScale);
        if (hostPixelSize.X <= 0 || hostPixelSize.Y <= 0)
            return false;

        var normX = (localPixelPos.X - destRect.Left) / destRect.Width;
        var normY = (localPixelPos.Y - destRect.Top) / destRect.Height;

        targetPixelPos = new Vector2(
            Math.Clamp(normX * hostPixelSize.X, 0, hostPixelSize.X - 1),
            Math.Clamp(normY * hostPixelSize.Y, 0, hostPixelSize.Y - 1)
        );
        return true;
    }

    protected override void Draw(IRenderHandle renderHandle)
    {
        base.Draw(renderHandle);

        var handle = renderHandle.DrawingHandleScreen;
        var screenRect = new UIBox2(Vector2.Zero, PixelSize);

        // Dark CRT glass base
        handle.DrawRect(screenRect, Color.FromHex("#070B12"));

        if (_isCycle)
        {
            Texture? cycleTex = null;
            if (!string.IsNullOrEmpty(_deviceGuid))
            {
                var surface = PipSurfaceRegistry.ResolveSurface(this, _deviceGuid);
                surface?.RequestCaptureFrame(4);
                cycleTex = surface?.ReadTexture;
            }

            var destRect = GetFittedScreenRect();
            if (cycleTex != null)
            {
                handle.DrawTextureRect(cycleTex, destRect);
            }
            else
            {
                handle.DrawRect(destRect, Color.FromHex("#05121E"));
            }

            DrawCrtOverlays(handle, destRect, isCycle: true);
            return;
        }

        if (GetCaptureTarget() is { } captureTarget && !captureTarget.Disposed && captureTarget.Visible)
        {
            var hostPixelSize = (Vector2i)(TargetResolution * UIScale);
            if (hostPixelSize.X <= 0 || hostPixelSize.Y <= 0)
                hostPixelSize = captureTarget.PixelSize;

            if (hostPixelSize.X > 0 && hostPixelSize.Y > 0)
                _surface.CaptureControl(renderHandle, captureTarget, hostPixelSize);
        }

        var screenTex = _surface.ReadTexture;
        var fittedRect = GetFittedScreenRect();
        if (screenTex != null)
        {
            handle.DrawTextureRect(screenTex, fittedRect);
        }

        DrawCrtOverlays(handle, fittedRect, isCycle: false);
    }

    private void DrawCrtOverlays(DrawingHandleScreen handle, UIBox2 screenRect, bool isCycle)
    {
        // Scanlines overlay: fine horizontal raster bands
        var scanlineColor = new Color(0, 0, 0, 0.12f);
        for (float y = screenRect.Top; y < screenRect.Bottom; y += 4f)
        {
            handle.DrawRect(new UIBox2(screenRect.Left, y, screenRect.Right, y + 1.5f), scanlineColor);
        }

        // Phosphor edge vignette
        var edgeColor = isCycle ? Color.FromHex("#FF3355") : Color.FromHex("#00D4FF");
        var alpha = isCycle ? 0.45f : 0.20f;
        handle.DrawRect(screenRect, edgeColor.WithAlpha(alpha), filled: false);

        // Inner border bevel
        if (screenRect.Width > 2f && screenRect.Height > 2f)
        {
            var innerRect = new UIBox2(screenRect.Left + 1f, screenRect.Top + 1f, screenRect.Right - 1f, screenRect.Bottom - 1f);
            handle.DrawRect(innerRect, Color.FromHex("#000000").WithAlpha(0.35f), filled: false);
        }
    }

    #region Input Event Relaying

    public static (Control control, Vector2 relPixelPos)? FindControlAtPos(Control control, Vector2 position)
    {
        if (!control.Visible)
            return null;

        for (var i = control.ChildCount - 1; i >= 0; i--)
        {
            var child = control.GetChild(i);
            if (!child.Visible)
                continue;

            if (child.RectClipContent && !child.PixelRect.Contains((Vector2i)position))
                continue;

            var childRelPos = position - child.PixelPosition;
            var maybeFound = FindControlAtPos(child, childRelPos);
            if (maybeFound != null)
                return maybeFound;
        }

        if (control.MouseFilter != Control.MouseFilterMode.Ignore &&
            position.X >= 0 && position.X <= control.PixelWidth &&
            position.Y >= 0 && position.Y <= control.PixelHeight)
        {
            return (control, position);
        }

        return null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        if (!TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            return;

        var hit = FindControlAtPos(targetRoot, targetPixelPos);
        if (hit == null)
            return;

        var (leafControl, leafPixelPos) = hit.Value;
        var windowId = args.PointerLocation.Window;

        var curPixelPos = leafPixelPos;
        Control? cur = leafControl;
        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                var curVirtualPos = curPixelPos / cur.UIScale;
                var pointerLoc = new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, windowId);
                var curEvent = new GUIBoundKeyEventArgs(
                    args.Function,
                    args.State,
                    pointerLoc,
                    args.CanFocus,
                    curVirtualPos,
                    curPixelPos);

                s_ControlKeyBindDown?.Invoke(cur, new object[] { curEvent });

                if (cur.CanKeyboardFocus && cur.KeyboardFocusOnClick)
                {
                    cur.GrabKeyboardFocus();
                }

                if (curEvent.Handled || cur.MouseFilter == MouseFilterMode.Stop)
                {
                    _relayedPressedControls[args.Function] = cur;
                    break;
                }
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }

        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        Control? targetControl = null;
        Vector2 leafPixelPos = Vector2.Zero;

        if (_relayedPressedControls.Remove(args.Function, out var pressed) && pressed.VisibleInTree)
        {
            targetControl = pressed;
            if (TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            {
                leafPixelPos = targetPixelPos - (pressed.GlobalPixelPosition - targetRoot.GlobalPixelPosition);
            }
        }
        else if (TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
        {
            var hit = FindControlAtPos(targetRoot, targetPixelPos);
            if (hit != null)
            {
                targetControl = hit.Value.control;
                leafPixelPos = hit.Value.relPixelPos;
            }
        }

        if (targetControl == null)
            return;

        var windowId = args.PointerLocation.Window;
        var curPixelPos = leafPixelPos;
        Control? cur = targetControl;
        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                var curVirtualPos = curPixelPos / cur.UIScale;
                var pointerLoc = new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, windowId);
                var curEvent = new GUIBoundKeyEventArgs(
                    args.Function,
                    args.State,
                    pointerLoc,
                    args.CanFocus,
                    curVirtualPos,
                    curPixelPos);

                // Ensure BaseButton attempting press counter is at least 1 so KeyBindUp triggers OnPressed
                if (cur is BaseButton btn && !btn.Disabled && s_ButtonAttemptingPress != null)
                {
                    var attempting = (int)(s_ButtonAttemptingPress.GetValue(btn) ?? 0);
                    if (attempting <= 0)
                        s_ButtonAttemptingPress.SetValue(btn, 1);
                }

                s_ControlKeyBindUp?.Invoke(cur, new object[] { curEvent });

                if (curEvent.Handled || cur.MouseFilter == MouseFilterMode.Stop)
                    break;
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }

        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        if (!TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            return;

        var hit = FindControlAtPos(targetRoot, targetPixelPos);
        if (hit == null)
            return;

        var (leafControl, leafPixelPos) = hit.Value;
        var windowId = args.GlobalPixelPosition.Window;

        var curPixelPos = leafPixelPos;
        Control? cur = leafControl;
        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                var curVirtualPos = curPixelPos / cur.UIScale;
                var pointerLoc = new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, windowId);
                var curEvent = new GUIMouseMoveEventArgs(
                    args.Relative / cur.UIScale,
                    cur,
                    curVirtualPos,
                    pointerLoc,
                    curVirtualPos,
                    curPixelPos);

                s_ControlMouseMove?.Invoke(cur, new object[] { curEvent });

                if (curEvent.Handled || cur.MouseFilter == MouseFilterMode.Stop)
                    break;
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        if (!TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            return;

        var hit = FindControlAtPos(targetRoot, targetPixelPos);
        if (hit == null)
            return;

        var (leafControl, leafPixelPos) = hit.Value;
        var windowId = args.GlobalPixelPosition.Window;

        var curPixelPos = leafPixelPos;
        Control? cur = leafControl;
        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                var curVirtualPos = curPixelPos / cur.UIScale;
                var pointerLoc = new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, windowId);
                var curEvent = new GUIMouseWheelEventArgs(
                    args.Delta,
                    cur,
                    curVirtualPos,
                    pointerLoc,
                    curVirtualPos,
                    curPixelPos);

                s_ControlMouseWheel?.Invoke(cur, new object[] { curEvent });

                if (curEvent.Handled || cur.MouseFilter == MouseFilterMode.Stop)
                    break;
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }

        args.Handle();
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeActiveTarget();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Generic abstraction for any UI that can be streamed into a <see cref=\"RemoteScreenView\"/>.
    /// </summary>
    public interface IRemoteTargetUi : IDisposable
    {
        Control? RootControl { get; }
        Vector2 TargetSize { get; }
        void UpdateState(RemoteScreenState state);
        void FrameUpdate(FrameEventArgs args);
        void SetHostBui(BoundUserInterface? hostBui);
    }

    /// <summary>
    /// Embeds any BoundUserInterface device (e.g. APC, Air Alarm, Static Computer, Consoles, PDAs, etc.)
    /// directly using the target entity's real client BoundUserInterface instance.
    /// </summary>
    public sealed class RemoteBuiTarget : IRemoteTargetUi
    {
        private readonly IEntityManager _entManager;
        private readonly IPlayerManager _playerManager;
        private readonly IUserInterfaceManager _uiManager;

        public NetEntity TargetNetEntity { get; }
        public Enum UiKey { get; }
        private readonly string _deviceName;

        private EntityUid? _targetUid;
        private RoutedBoundUserInterface? _routedBui;
        private BoundUserInterfaceState? _latestState;

        public Control? RootControl => _routedBui?.RootControl;
        public Vector2 TargetSize { get; private set; } = new Vector2(640, 520);

        public RemoteBuiTarget(
            NetEntity targetNetEntity,
            Enum uiKey,
            string deviceName,
            IEntityManager entManager,
            IPlayerManager playerManager,
            IUserInterfaceManager uiManager)
        {
            TargetNetEntity = targetNetEntity;
            UiKey = uiKey;
            _deviceName = deviceName;
            _entManager = entManager;
            _playerManager = playerManager;
            _uiManager = uiManager;
        }

        public bool Matches(NetEntity target, Enum uiKey)
        {
            return TargetNetEntity == target && Equals(UiKey, uiKey);
        }

        public void SetHostBui(BoundUserInterface? hostBui)
        {
        }

        public void UpdateState(RemoteScreenState state)
        {
            if (state.BuiState != null)
                _latestState = state.BuiState;

            TryEnsureBui();
            ApplyLatestState();
        }

        public void FrameUpdate(FrameEventArgs args)
        {
            if (_routedBui == null || !_routedBui.IsOpened)
                TryEnsureBui();

            if (_routedBui?.RootControl is ApcMenu apcMenu)
                apcMenu.SetAccessEnabled(true);
        }

        private void TryEnsureBui()
        {
            if (_targetUid == null || !_entManager.EntityExists(_targetUid.Value))
            {
                if (!_entManager.TryGetEntity(TargetNetEntity, out var resolved) || !resolved.HasValue)
                    return;
                _targetUid = resolved.Value;
            }

            var uid = _targetUid.Value;
            if (!_entManager.TryGetComponent<UserInterfaceComponent>(uid, out var uiComp))
                return;

            uiComp.ClientOpenInterfaces.TryGetValue(UiKey, out var currentBui);
            var routed = currentBui as RoutedBoundUserInterface;

            if (_routedBui != routed)
            {
                if (_routedBui != null)
                    _routedBui.OnRootChanged -= OnRootChanged;

                _routedBui = routed;

                if (_routedBui != null)
                {
                    _routedBui.OnRootChanged += OnRootChanged;
                    if (_routedBui.RootControl != null)
                        OnTargetRootAttached(_routedBui.RootControl);
                }
            }
            else if (_routedBui != null && _routedBui.RootControl != null)
            {
                OnTargetRootAttached(_routedBui.RootControl);
            }
        }

        private void OnRootChanged(Control? root)
        {
            if (root != null)
                OnTargetRootAttached(root);
        }

        private void OnTargetRootAttached(Control control)
        {
            if (control.Disposed)
                return;

            if (control is ApcMenu apcMenu)
                apcMenu.SetAccessEnabled(true);

            ApplyLatestState();
        }

        private void ApplyLatestState()
        {
            if (_targetUid is { } uid && _latestState != null)
            {
                var uiSys = _entManager.System<UserInterfaceSystem>();
                uiSys.SetUiState(uid, UiKey, _latestState);
            }

            if (_routedBui?.RootControl is ApcMenu apcMenu)
            {
                if (_latestState is ApcBoundInterfaceState apcState)
                    apcMenu.UpdateState(apcState);
                apcMenu.SetAccessEnabled(true);
            }
            else if (_routedBui?.RootControl is PdaMenu pdaMenu)
            {
                if (_latestState is PdaUpdateState pdaState)
                    pdaMenu.UpdateState(pdaState);
            }
        }

        public void Dispose()
        {
            if (_routedBui != null)
            {
                _routedBui.OnRootChanged -= OnRootChanged;
                _routedBui = null;
            }
        }
    }
}
