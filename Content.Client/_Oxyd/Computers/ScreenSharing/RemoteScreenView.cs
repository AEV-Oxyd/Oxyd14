using System.Numerics;
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
/// (mouse clicks, hovers, scroll, keyboard typing, focus) directly to the target UI without reflection.
/// Infinite recursive cycles (A -> B -> A or A -> A) are rendered via Picture-in-Picture (PiP) mirror textures.
/// </summary>
public sealed partial class RemoteScreenView : LayoutContainer
{
    private static LayoutContainer? s_OffscreenStash;

    /// <summary>
    /// Dedicated offscreen container parented to WindowRoot at an unreachable negative coordinate.
    /// Allows remote target windows to maintain active layout, measurement, and style resolution
    /// while remaining completely invisible to the local player's screen and scissor box.
    /// </summary>
    public static LayoutContainer GetOrCreateOffscreenStash(IUserInterfaceManager uiManager)
    {
        if (s_OffscreenStash != null && !s_OffscreenStash.Disposed && s_OffscreenStash.Parent != null)
            return s_OffscreenStash;

        s_OffscreenStash = new LayoutContainer
        {
            Name = "RemoteOffscreenStash",
            RectClipContent = true,
            SetSize = new Vector2(2000, 2000)
        };

        uiManager.WindowRoot.AddChild(s_OffscreenStash);
        LayoutContainer.SetPosition(s_OffscreenStash, new Vector2(-30000, -30000));
        return s_OffscreenStash;
    }

    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

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

    private Control? _lastHoveredRemoteControl;
    private readonly Dictionary<BoundKeyFunction, (IInputRelayTarget relay, Control control)> _remoteHeldControls = new();

    public bool IsPoppedOut => _activeTargetUi?.IsPoppedOut ?? false;

    public void TogglePopOut()
    {
        _activeTargetUi?.TogglePopOut();
    }

    public void SetPoppedOut(bool poppedOut)
    {
        _activeTargetUi?.SetPoppedOut(poppedOut);
    }

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
        MouseFilter = MouseFilterMode.Pass;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;

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
                      PipSurfaceRegistry.IsDeviceInAncestorChain(this, state.DeviceGuid) ||
                      (_hostBui != null && _entManager.TryGetEntity(state.TargetEntity, out var tgtUid) && tgtUid == _hostBui.Owner);

        if (isCycle)
        {
            DisposeActiveTarget();
            _isCycle = true;
            _surface.Visible = false;
            _surface.DeviceGuid = null;
            _statusLabel.Visible = false;
            ClearHoveredRemoteControl();

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
        _activeTargetUi.OnPopOutChanged += UpdatePopOutButton;
        UpdatePopOutButton(_activeTargetUi.IsPoppedOut);

        _statusLabel.Visible = false;
        _surface.InvalidateCapture();
    }

    private void UpdatePopOutButton(bool isPoppedOut)
    {
    }

    private void ClearHoveredRemoteControl()
    {
        if (_lastHoveredRemoteControl is IInputRelayTarget relay)
        {
            relay.RelayMouseExited();
        }

        _lastHoveredRemoteControl = null;
        _remoteHeldControls.Clear();
    }

    private void DisposeActiveTarget()
    {
        ClearHoveredRemoteControl();

        if (_activeTargetUi != null)
        {
            _activeTargetUi.OnPopOutChanged -= UpdatePopOutButton;
            _activeTargetUi.Dispose();
            _activeTargetUi = null;
        }

        UpdatePopOutButton(false);
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

    protected override void MouseExited()
    {
        base.MouseExited();
        ClearHoveredRemoteControl();
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

        var isDynamicCycle = false;
        if (GetCaptureTarget() is { } captureTarget && !captureTarget.Disposed && captureTarget.Visible)
        {
            if (_surface.CanCapture(captureTarget, _deviceGuid))
            {
                var hostPixelSize = (Vector2i)(TargetResolution * UIScale);
                if (hostPixelSize.X <= 0 || hostPixelSize.Y <= 0)
                    hostPixelSize = captureTarget.PixelSize;

                if (hostPixelSize.X > 0 && hostPixelSize.Y > 0)
                    _surface.CaptureControl(renderHandle, captureTarget, hostPixelSize);
            }
            else
            {
                // Cycle boundary reached in the active capture stack (e.g., A -> B -> A or A -> A):
                // Do not recurse further; sample the previous bounce's texture from our ping-pong read buffer!
                isDynamicCycle = true;
            }
        }

        var screenTex = _surface.ReadTexture;
        if (screenTex == null && !string.IsNullOrEmpty(_deviceGuid))
        {
            var fallbackSurface = PipSurfaceRegistry.ResolveSurface(this, _deviceGuid);
            fallbackSurface?.RequestCaptureFrame(4);
            screenTex = fallbackSurface?.ReadTexture;
        }

        var fittedRect = GetFittedScreenRect();
        if (screenTex != null)
        {
            handle.DrawTextureRect(screenTex, fittedRect);
        }
        else if (isDynamicCycle)
        {
            handle.DrawRect(fittedRect, Color.FromHex("#05121E"));
        }

        DrawCrtOverlays(handle, fittedRect, isCycle: isDynamicCycle);
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

    #region Input Event Relaying (Zero-Reflection)

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
        if (args.Handled)
            return;

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        if (!TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            return;

        var hit = FindControlAtPos(targetRoot, targetPixelPos);
        if (hit == null)
            return;

        var (leafControl, leafPixelPos) = hit.Value;
        var curPixelPos = leafPixelPos;
        Control? cur = leafControl;

        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                if (cur.CanKeyboardFocus && cur.KeyboardFocusOnClick)
                {
                    cur.GrabKeyboardFocus();
                }

                if (cur is Slider slider && !slider.Disabled && args.Function == EngineKeyFunctions.UIClick)
                {
                    var ratio = Math.Clamp(curPixelPos.X / Math.Max(1f, cur.PixelWidth), 0f, 1f);
                    slider.Value = slider.MinValue + ratio * (slider.MaxValue - slider.MinValue);
                    break;
                }

                if (cur is TabContainer tabContainer && args.Function == EngineKeyFunctions.UIClick)
                {
                    if (curPixelPos.Y <= 32f * tabContainer.UIScale && tabContainer.ChildCount > 0)
                    {
                        var tabWidth = Math.Max(1f, (float)tabContainer.PixelWidth / tabContainer.ChildCount);
                        var tabIdx = Math.Clamp((int)(curPixelPos.X / tabWidth), 0, tabContainer.ChildCount - 1);
                        tabContainer.CurrentTab = tabIdx;
                        break;
                    }
                }

                if (cur is ScrollBar scrollBar && args.Function == EngineKeyFunctions.UIClick)
                {
                    var ratio = Math.Clamp(curPixelPos.Y / Math.Max(1f, scrollBar.PixelHeight), 0f, 1f);
                    scrollBar.ValueTarget = scrollBar.MinValue + ratio * (scrollBar.MaxValue - scrollBar.MinValue);
                    break;
                }

                if (cur is IInputRelayTarget relay)
                {
                    var relPos = curPixelPos / cur.UIScale;
                    var targetArgs = new GUIBoundKeyEventArgs(
                        args.Function,
                        args.State,
                        new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, default),
                        args.CanFocus,
                        relPos,
                        curPixelPos);

                    relay.RelayKeyBindDown(targetArgs);
                    _remoteHeldControls[args.Function] = (relay, cur);

                    if (targetArgs.Handled)
                    {
                        args.Handle();
                        return;
                    }
                }

                if (cur.MouseFilter == MouseFilterMode.Stop)
                    break;
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }

        _surface.InvalidateCapture(2);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Handled)
            return;

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
            return;

        if (_remoteHeldControls.Remove(args.Function, out var held))
        {
            var (heldRelay, heldControl) = held;
            var relPos = Vector2.Zero;
            var curPixelPos = Vector2.Zero;
            if (TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
            {
                var ctrl = heldControl;
                var offset = Vector2.Zero;
                while (ctrl != null && ctrl != targetRoot)
                {
                    offset += ctrl.PixelPosition;
                    ctrl = ctrl.Parent;
                }
                curPixelPos = targetPixelPos - offset;
                relPos = curPixelPos / heldControl.UIScale;
            }

            var targetArgs = new GUIBoundKeyEventArgs(
                args.Function,
                args.State,
                new ScreenCoordinates(heldControl.GlobalPixelPosition + curPixelPos, default),
                args.CanFocus,
                relPos,
                curPixelPos);

            heldRelay.RelayKeyBindUp(targetArgs);

            if (targetArgs.Handled)
            {
                args.Handle();
                return;
            }
        }
        else if (TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
        {
            var hit = FindControlAtPos(targetRoot, targetPixelPos);
            if (hit != null)
            {
                var (leafControl, leafPixelPos) = hit.Value;
                var curPixelPos = leafPixelPos;
                Control? cur = leafControl;

                while (cur != null)
                {
                    if (cur.MouseFilter != MouseFilterMode.Ignore)
                    {
                        if (cur is IInputRelayTarget relay)
                        {
                            var relPos = curPixelPos / cur.UIScale;
                            var targetArgs = new GUIBoundKeyEventArgs(
                                args.Function,
                                args.State,
                                new ScreenCoordinates(cur.GlobalPixelPosition + curPixelPos, default),
                                args.CanFocus,
                                relPos,
                                curPixelPos);

                            relay.RelayKeyBindUp(targetArgs);

                            if (targetArgs.Handled)
                            {
                                args.Handle();
                                return;
                            }
                        }

                        if (cur.MouseFilter == MouseFilterMode.Stop)
                            break;
                    }

                    if (cur == targetRoot)
                        break;

                    curPixelPos += cur.PixelPosition;
                    cur = cur.Parent;
                }
            }
        }

        _surface.InvalidateCapture(2);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (GetCaptureTarget() is not { } targetRoot || _isCycle)
        {
            ClearHoveredRemoteControl();
            return;
        }

        if (!TryMapToTarget(args.RelativePixelPosition, out var targetPixelPos))
        {
            ClearHoveredRemoteControl();
            return;
        }

        var hit = FindControlAtPos(targetRoot, targetPixelPos);
        var newHovered = hit?.control;

        if (_lastHoveredRemoteControl != newHovered)
        {
            if (_lastHoveredRemoteControl is IInputRelayTarget oldRelay)
            {
                oldRelay.RelayMouseExited();
            }

            _lastHoveredRemoteControl = newHovered;

            if (_lastHoveredRemoteControl is IInputRelayTarget newRelay)
            {
                newRelay.RelayMouseEntered();
            }

            _surface.InvalidateCapture(2);
        }

        if (newHovered is IInputRelayTarget relay && hit != null)
        {
            var targetArgs = new GUIMouseMoveEventArgs(
                args.Relative,
                newHovered,
                args.GlobalPosition,
                args.GlobalPixelPosition,
                hit.Value.relPixelPos / newHovered.UIScale,
                hit.Value.relPixelPos);

            relay.RelayMouseMove(targetArgs);
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
        var curPixelPos = leafPixelPos;
        Control? cur = leafControl;

        while (cur != null)
        {
            if (cur.MouseFilter != MouseFilterMode.Ignore)
            {
                var targetArgs = new GUIMouseWheelEventArgs(
                    args.Delta,
                    cur,
                    args.GlobalPosition,
                    args.GlobalPixelPosition,
                    curPixelPos / cur.UIScale,
                    curPixelPos);

                if (cur is IInputRelayTarget relay)
                {
                    relay.RelayMouseWheel(targetArgs);
                }
                else if (cur is ScrollContainer sc)
                {
                    if (sc.VScrollEnabled)
                    {
                        sc.VScrollTarget -= args.Delta.Y * sc.ScrollSpeedY;
                        targetArgs.Handle();
                    }
                    else if (sc.HScrollEnabled)
                    {
                        var delta = args.Delta.X == 0f && sc.FallbackDeltaScroll ? -args.Delta.Y : args.Delta.X;
                        sc.HScrollTarget += delta * sc.ScrollSpeedX;
                        targetArgs.Handle();
                    }
                }
                else if (cur is ScrollBar sb)
                {
                    sb.ValueTarget -= args.Delta.Y * 50f;
                    targetArgs.Handle();
                }

                // Important: Only break if the wheel event was actually handled.
                // Do NOT break on MouseFilterMode.Stop so the mouse wheel event can bubble up
                // from child elements (buttons, panels, labels) to their parent scroll container.
                if (targetArgs.Handled)
                    break;
            }

            if (cur == targetRoot)
                break;

            curPixelPos += cur.PixelPosition;
            cur = cur.Parent;
        }

        _surface.InvalidateCapture(2);
        args.Handle();
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        base.TextEntered(args);

        if (_uiManager.KeyboardFocused is IInputRelayTarget relay)
        {
            relay.RelayTextEntered(args);
            _surface.InvalidateCapture(2);
        }
    }

    protected override void TextEditing(GUITextEditingEventArgs args)
    {
        base.TextEditing(args);

        if (_uiManager.KeyboardFocused is IInputRelayTarget relay)
        {
            relay.RelayTextEditing(args);
            _surface.InvalidateCapture(2);
        }
    }

    protected override void KeyHeld(GUIKeyEventArgs args)
    {
        base.KeyHeld(args);

        if (_uiManager.KeyboardFocused is IInputRelayTarget relay)
        {
            relay.RelayKeyHeld(args);
            _surface.InvalidateCapture(2);
        }
    }

    #endregion

    [Obsolete("Types overriding Dispose(bool) are discouraged in Content.Client; preserved for layout container cleanup.")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeActiveTarget();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Generic abstraction for any UI that can be streamed into a <see cref="RemoteScreenView"/>.
    /// </summary>
    public interface IRemoteTargetUi : IDisposable
    {
        Control? RootControl { get; }
        Vector2 TargetSize { get; }
        bool CanPopOut { get; }
        bool IsPoppedOut { get; }
        event Action<bool>? OnPopOutChanged;
        void TogglePopOut();
        void SetPoppedOut(bool poppedOut);
        void UpdateState(RemoteScreenState state);
        void FrameUpdate(FrameEventArgs args);
        void SetHostBui(BoundUserInterface? hostBui);
    }

    /// <summary>
    /// Invisible overlay placed on top of a remote window's close button to intercept close clicks.
    /// Redirects the close action into docking the window back offscreen without triggering bui.Close().
    /// </summary>
    private sealed class RemoteCloseButtonInterceptor : Control
    {
        private readonly Action _onIntercept;

        public RemoteCloseButtonInterceptor(Action onIntercept)
        {
            _onIntercept = onIntercept;
            MouseFilter = MouseFilterMode.Stop;
            HorizontalExpand = true;
            VerticalExpand = true;
            ToolTip = "Dock back into terminal";
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function == EngineKeyFunctions.UIClick)
            {
                args.Handle();
                UserInterfaceManager.ClickSound();
                _onIntercept();
            }
        }
    }

    /// <summary>
    /// Embeds any BoundUserInterface device (e.g. APC, Air Alarm, Static Computer, Consoles, PDAs, etc.)
    /// directly using the target entity's real client BoundUserInterface instance.
    /// Manages offscreen stashing to prevent duplicate desktop popups while keeping BUI communication active.
    /// </summary>
    public sealed class RemoteBuiTarget : IRemoteTargetUi
    {
        private static readonly Dictionary<EntityUid, RemoteBuiTarget> s_ActiveTargetsByUid = new();

        public static bool TryBringTargetToScreen(EntityUid uid)
        {
            if (s_ActiveTargetsByUid.TryGetValue(uid, out var target))
            {
                if (!target.IsPoppedOut)
                {
                    target.PullWindowOnScreen();
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetForEntity(EntityUid uid, out RemoteBuiTarget target)
        {
            return s_ActiveTargetsByUid.TryGetValue(uid, out target!);
        }

        private static int s_ApplyStateDepth;

        private readonly IEntityManager _entManager;
        private readonly IPlayerManager _playerManager;
        private readonly IUserInterfaceManager _uiManager;

        public NetEntity TargetNetEntity { get; }
        public Enum UiKey { get; }
        private readonly string _deviceName;

        private EntityUid? _targetUid;
        private BoundUserInterface? _currentBui;
        private RoutedBoundUserInterface? _routedBui;
        private BoundUserInterfaceState? _latestState;

        private BaseWindow? _trackedWindow;
        private BaseButton? _trackedCloseButton;
        private RemoteCloseButtonInterceptor? _closeInterceptor;
        private bool _isPoppedOut;
        public Control? RootControl => _routedBui?.RootControl;
        public Vector2 TargetSize { get; private set; } = new Vector2(640, 520);

        public bool CanPopOut => _trackedWindow != null && !_trackedWindow.Disposed;
        public bool IsPoppedOut => _isPoppedOut;
        public event Action<bool>? OnPopOutChanged;

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
            if (state.BuiState != null)
                ApplyLatestState();
        }

        public void FrameUpdate(FrameEventArgs args)
        {
            if (_currentBui == null || !_currentBui.IsOpened)
                TryEnsureBui();

            if (_trackedWindow == null && RootControl != null)
                AttachWindow(RootControl);

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
            s_ActiveTargetsByUid[uid] = this;

            if (!_entManager.TryGetComponent<UserInterfaceComponent>(uid, out var uiComp))
                return;

            uiComp.ClientOpenInterfaces.TryGetValue(UiKey, out var currentBui);

            if (_currentBui != currentBui)
            {
                if (_routedBui != null)
                    _routedBui.OnRootChanged -= OnRootChanged;

                _currentBui = currentBui;
                _routedBui = currentBui as RoutedBoundUserInterface;

                if (_routedBui != null)
                {
                    _routedBui.OnRootChanged += OnRootChanged;
                    if (_routedBui.RootControl != null)
                        OnTargetRootAttached(_routedBui.RootControl);
                }
            }
            else if (_trackedWindow == null && RootControl != null)
            {
                OnTargetRootAttached(RootControl);
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

            AttachWindow(control);

            if (control is ApcMenu apcMenu)
                apcMenu.SetAccessEnabled(true);

            ApplyLatestState();
        }

        private void AttachWindow(Control root)
        {
            var window = FindTargetWindow(root);
            if (window == _trackedWindow && _trackedWindow != null)
                return;

            DetachWindow();

            _trackedWindow = window;

            if (_trackedWindow != null)
            {
                _trackedCloseButton = FindCloseButton(_trackedWindow);
                if (_trackedCloseButton != null && _closeInterceptor == null)
                {
                    _closeInterceptor = new RemoteCloseButtonInterceptor(StashWindowOffscreen);
                    _trackedCloseButton.AddChild(_closeInterceptor);
                    LayoutContainer.SetAnchorAndMarginPreset(_closeInterceptor, LayoutPreset.Wide);
                }

                // If the target UI is ALREADY open on the user's screen:
                // DO NOT move it off screen unless closed!
                var isAlreadyOnScreen = _trackedWindow.IsOpen &&
                                        _trackedWindow.Visible &&
                                        _trackedWindow.Parent != null &&
                                        _trackedWindow.Parent != s_OffscreenStash;

                if (isAlreadyOnScreen)
                {
                    _isPoppedOut = true;
                }
                else if (!_isPoppedOut)
                {
                    StashWindowOffscreen();
                }
            }

            OnPopOutChanged?.Invoke(_isPoppedOut);
        }

        private void DetachWindow()
        {
            if (_closeInterceptor != null)
            {
                _closeInterceptor.Parent?.RemoveChild(_closeInterceptor);
                _closeInterceptor.Dispose();
                _closeInterceptor = null;
            }

            _trackedCloseButton = null;
            _trackedWindow = null;
        }

        private static BaseWindow? FindTargetWindow(Control? control)
        {
            if (control == null)
                return null;

            if (control is BaseWindow bw)
                return bw;

            for (var cur = control.Parent; cur != null; cur = cur.Parent)
            {
                if (cur is BaseWindow parentBw)
                    return parentBw;
            }

            return null;
        }

        private static BaseButton? FindCloseButton(Control control)
        {
            if (control.Name == "CloseButton" && control is BaseButton btn)
                return btn;

            for (var i = 0; i < control.ChildCount; i++)
            {
                var found = FindCloseButton(control.GetChild(i));
                if (found != null)
                    return found;
            }

            return null;
        }

        public void StashWindowOffscreen()
        {
            _isPoppedOut = false;
            if (_trackedWindow == null || _trackedWindow.Disposed)
            {
                OnPopOutChanged?.Invoke(_isPoppedOut);
                return;
            }

            var stash = GetOrCreateOffscreenStash(_uiManager);
            if (_trackedWindow.Parent != stash)
            {
                _trackedWindow.Parent?.RemoveChild(_trackedWindow);
                stash.AddChild(_trackedWindow);
                LayoutContainer.SetPosition(_trackedWindow, Vector2.Zero);
            }

            OnPopOutChanged?.Invoke(_isPoppedOut);
        }

        public void PullWindowOnScreen()
        {
            _isPoppedOut = true;
            if (_trackedWindow == null || _trackedWindow.Disposed)
            {
                OnPopOutChanged?.Invoke(_isPoppedOut);
                return;
            }

            if (_trackedWindow.Parent != _uiManager.WindowRoot)
            {
                _trackedWindow.Parent?.RemoveChild(_trackedWindow);
                _uiManager.WindowRoot.AddChild(_trackedWindow);
            }

            _trackedWindow.Visible = true;
            _trackedWindow.OpenCentered();

            OnPopOutChanged?.Invoke(_isPoppedOut);
        }

        public void TogglePopOut()
        {
            if (_isPoppedOut)
                StashWindowOffscreen();
            else
                PullWindowOnScreen();
        }

        public void SetPoppedOut(bool poppedOut)
        {
            if (poppedOut)
                PullWindowOnScreen();
            else
                StashWindowOffscreen();
        }

        private void ApplyLatestState()
        {
            if (s_ApplyStateDepth > 0 || _latestState == null)
                return;

            s_ApplyStateDepth++;
            try
            {
                if (_targetUid is { } uid)
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
            finally
            {
                s_ApplyStateDepth--;
            }
        }

        public void Dispose()
        {
            if (_targetUid.HasValue && s_ActiveTargetsByUid.TryGetValue(_targetUid.Value, out var existing) && existing == this)
            {
                s_ActiveTargetsByUid.Remove(_targetUid.Value);
            }

            DetachWindow();

            var stash = s_OffscreenStash;
            if (stash != null && _trackedWindow != null && _trackedWindow.Parent == stash)
            {
                stash.RemoveChild(_trackedWindow);
            }

            if (_routedBui != null)
            {
                _routedBui.OnRootChanged -= OnRootChanged;
            }

            if (_currentBui != null)
            {
                _currentBui = null;
                _routedBui = null;
            }
        }
    }
}
