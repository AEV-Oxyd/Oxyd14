using System.Numerics;
using Content.Client._Oxyd.Computers.ScreenSharing;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A scrollable container derived from <see cref="ContentControl"/> that supports scrolling via mouse wheel
/// and scrollbar dragging, renders a clean foreground scrollbar over scrollable content,
/// and relays events cleanly in remote screen views.
/// </summary>
public class ContentScrollContainer : ContentControl
{
    private const float ScrollBarLogicalWidth = 12f;
    private const float InsetPadding = 2f;

    public bool HScrollEnabled { get; set; } = false;
    public bool VScrollEnabled { get; set; } = true;
    public bool HScrollBarHidden { get; set; } = false;

    /// <summary>
    /// When true, renders the scrollbar track/thumb whenever content overflows vertically.
    /// </summary>
    public bool ShowScrollVisuals { get; set; } = true;

    /// <summary>
    /// Retained for API compatibility; background texture/well fill is intentionally omitted
    /// so scrollable containers blend cleanly with surrounding UI elements.
    /// </summary>
    public bool AlwaysShowBackground { get; set; } = false;

    private float _scrollOffsetX;
    public float HScroll
    {
        get => _scrollOffsetX;
        set
        {
            _scrollOffsetX = Math.Max(0f, value);
            InvalidateArrange();
            InvalidateAncestorPipSurface();
        }
    }

    private float _scrollOffsetY;
    public float ScrollOffsetY
    {
        get => _scrollOffsetY;
        set
        {
            _scrollOffsetY = Math.Max(0f, value);
            InvalidateArrange();
            InvalidateAncestorPipSurface();
        }
    }

    private float _lastContentHeight;
    private float _lastViewportHeight;
    private float _lastMaxScrollY;
    private float _lastMaxScrollX;
    private bool _draggingVThumb;
    private bool _hoveringVBar;
    private float _dragStartPixelY;
    private float _dragStartScrollY;
    private readonly Stack<Matrix3x2> _drawTransformStack = new();

    public bool IsVerticallyScrollable => VScrollEnabled && _lastMaxScrollY > 0.5f;
    public bool IsHorizontallyScrollable => HScrollEnabled && _lastMaxScrollX > 0.5f;
    public float MaxScrollY => _lastMaxScrollY;

    public ContentScrollContainer()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    private void InvalidateAncestorPipSurface()
    {
        for (var cur = Parent; cur != null; cur = cur.Parent)
        {
            if (cur is PipScreenSurface surface && !surface.Disposed)
            {
                surface.InvalidateCapture(2);
                PipSurfaceRegistry.InvalidateDevice(surface.DeviceGuid, 2);
                break;
            }
        }
    }

    private UIBox2 GetVerticalTrackPixelRect()
    {
        var pad = InsetPadding * UIScale;
        var barW = (ScrollBarLogicalWidth - 2f) * UIScale;
        var left = MathF.Max(pad, PixelWidth - barW - pad);
        var top = pad + 2f * UIScale;
        var bottom = MathF.Max(top + 8f, PixelHeight - pad - 2f * UIScale);
        return new UIBox2(left, top, MathF.Max(left + 4f, PixelWidth - pad), bottom);
    }

    private UIBox2 GetVerticalThumbPixelRect(UIBox2 track)
    {
        var trackH = MathF.Max(1f, track.Height);
        var contentH = MathF.Max(1f, _lastContentHeight);
        var viewportH = MathF.Max(1f, _lastViewportHeight);
        var thumbRatio = Math.Clamp(viewportH / contentH, 0.12f, 1f);
        var thumbH = MathF.Max(18f * UIScale, trackH * thumbRatio);
        thumbH = MathF.Min(thumbH, trackH);

        var scrollRatio = _lastMaxScrollY > 0.01f ? Math.Clamp(_scrollOffsetY / _lastMaxScrollY, 0f, 1f) : 0f;
        var thumbTop = track.Top + (trackH - thumbH) * scrollRatio;
        return new UIBox2(track.Left + 1f, thumbTop, track.Right - 1f, thumbTop + thumbH);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.Handled)
            return;

        if (VScrollEnabled && ChildCount > 0)
        {
            var child = GetChild(0);
            var padY = ShowScrollVisuals ? InsetPadding * 2f : 0f;
            var viewH = MathF.Max(0f, Height - padY);
            var maxScroll = Math.Max(0f, child.DesiredSize.Y - viewH);
            if (maxScroll > 0.5f)
            {
                _scrollOffsetY = Math.Clamp(_scrollOffsetY - args.Delta.Y * 34f, 0f, maxScroll);
                InvalidateArrange();
                InvalidateAncestorPipSurface();
                args.Handle();
            }
        }
        else if (HScrollEnabled && ChildCount > 0)
        {
            var child = GetChild(0);
            var maxScrollX = Math.Max(0f, child.DesiredSize.X - Width);
            if (maxScrollX > 0.5f)
            {
                var delta = args.Delta.X == 0f && !VScrollEnabled ? -args.Delta.Y : args.Delta.X;
                _scrollOffsetX = Math.Clamp(_scrollOffsetX + delta * 34f, 0f, maxScrollX);
                InvalidateArrange();
                InvalidateAncestorPipSurface();
                args.Handle();
            }
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick || !ShowScrollVisuals || !IsVerticallyScrollable)
            return;

        var track = GetVerticalTrackPixelRect();
        if (!track.Contains(args.RelativePixelPosition))
            return;

        var thumb = GetVerticalThumbPixelRect(track);
        if (thumb.Contains(args.RelativePixelPosition))
        {
            _draggingVThumb = true;
            _dragStartPixelY = args.RelativePixelPosition.Y;
            _dragStartScrollY = _scrollOffsetY;
        }
        else
        {
            // Jump scroll position to clicked location along the scrollbar track.
            var ratio = Math.Clamp((args.RelativePixelPosition.Y - track.Top) / MathF.Max(1f, track.Height), 0f, 1f);
            _scrollOffsetY = ratio * _lastMaxScrollY;
            _draggingVThumb = true;
            _dragStartPixelY = args.RelativePixelPosition.Y;
            _dragStartScrollY = _scrollOffsetY;
            InvalidateArrange();
        }

        InvalidateAncestorPipSurface();
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick && _draggingVThumb)
        {
            _draggingVThumb = false;
            InvalidateAncestorPipSurface();
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!ShowScrollVisuals || !IsVerticallyScrollable)
        {
            if (_hoveringVBar)
            {
                _hoveringVBar = false;
                InvalidateAncestorPipSurface();
            }
            return;
        }

        var track = GetVerticalTrackPixelRect();
        var nextHover = track.Contains(args.RelativePixelPosition);
        if (nextHover != _hoveringVBar)
        {
            _hoveringVBar = nextHover;
            InvalidateAncestorPipSurface();
        }

        if (_draggingVThumb)
        {
            var thumb = GetVerticalThumbPixelRect(track);
            var usableTrack = MathF.Max(1f, track.Height - thumb.Height);
            var deltaY = args.RelativePixelPosition.Y - _dragStartPixelY;
            var scrollDelta = (deltaY / usableTrack) * _lastMaxScrollY;
            _scrollOffsetY = Math.Clamp(_dragStartScrollY + scrollDelta, 0f, _lastMaxScrollY);
            InvalidateArrange();
            InvalidateAncestorPipSurface();
        }
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        if (_hoveringVBar)
        {
            _hoveringVBar = false;
            InvalidateAncestorPipSurface();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (ChildCount == 0)
            return Vector2.Zero;

        var child = GetChild(0);
        var padX = ShowScrollVisuals ? InsetPadding * 2f : 0f;
        var padY = ShowScrollVisuals ? InsetPadding * 2f : 0f;
        var reserveBar = ShowScrollVisuals && VScrollEnabled ? ScrollBarLogicalWidth : 0f;
        var childAvailX = HScrollEnabled
            ? float.PositiveInfinity
            : float.IsFinite(availableSize.X)
                ? MathF.Max(0f, availableSize.X - padX - reserveBar)
                : availableSize.X;

        var measureAvail = new Vector2(
            childAvailX,
            VScrollEnabled ? float.PositiveInfinity : MathF.Max(0f, availableSize.Y - padY));

        child.Measure(measureAvail);
        var desired = new Vector2(child.DesiredSize.X + padX + reserveBar, child.DesiredSize.Y + padY);
        return Vector2.Min(desired, availableSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (ChildCount == 0)
        {
            _lastContentHeight = 0f;
            _lastViewportHeight = finalSize.Y;
            _lastMaxScrollY = 0f;
            _lastMaxScrollX = 0f;
            return finalSize;
        }

        var child = GetChild(0);
        var pad = ShowScrollVisuals ? InsetPadding : 0f;
        var viewH = MathF.Max(0f, finalSize.Y - pad * 2f);
        var needsVBar = ShowScrollVisuals && VScrollEnabled && child.DesiredSize.Y > viewH + 0.5f;
        var barReserve = needsVBar ? ScrollBarLogicalWidth : 0f;
        var viewW = MathF.Max(0f, finalSize.X - pad * 2f - barReserve);

        var childW = HScrollEnabled ? Math.Max(child.DesiredSize.X, viewW) : viewW;
        var childH = VScrollEnabled ? Math.Max(child.DesiredSize.Y, viewH) : viewH;

        var prevScrollable = IsVerticallyScrollable;
        var prevMaxScrollY = _lastMaxScrollY;

        _lastContentHeight = childH;
        _lastViewportHeight = viewH;
        _lastMaxScrollY = Math.Max(0f, childH - viewH);
        _lastMaxScrollX = Math.Max(0f, childW - viewW);

        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0f, _lastMaxScrollY);
        _scrollOffsetX = Math.Clamp(_scrollOffsetX, 0f, _lastMaxScrollX);

        child.Arrange(new UIBox2(
            pad - _scrollOffsetX,
            pad - _scrollOffsetY,
            pad + childW - _scrollOffsetX,
            pad + childH - _scrollOffsetY));

        if (prevScrollable != IsVerticallyScrollable || MathF.Abs(prevMaxScrollY - _lastMaxScrollY) > 1f)
            InvalidateAncestorPipSurface();

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        // Capture exact local transform on a re-entrant stack so PostRenderChildren draws the scrollbar
        // on top of children in direct windows, offscreen remote captures, and nested PiP cascades.
        _drawTransformStack.Push(handle.GetTransform());
    }

    protected override void PostRenderChildren(ref ControlRenderArguments args)
    {
        base.PostRenderChildren(ref args);

        if (_drawTransformStack.Count == 0)
            return;

        var drawTransform = _drawTransformStack.Pop();
        if (!ShowScrollVisuals || !IsVerticallyScrollable)
            return;

        var handle = args.Handle.DrawingHandleScreen;
        var oldXform = handle.GetTransform();
        var oldMod = handle.Modulate;
        handle.SetTransform(drawTransform);
        handle.Modulate = args.Modulate * ActualModulateSelf;

        var track = GetVerticalTrackPixelRect();
        handle.DrawRect(track, Color.FromHex("#0E1E2C"), filled: true);
        handle.DrawRect(track, Color.FromHex("#2C6280"), filled: false);

        var thumb = GetVerticalThumbPixelRect(track);
        var thumbColor = _draggingVThumb
            ? Color.FromHex("#88ECFF")
            : _hoveringVBar
                ? Color.FromHex("#66D4F8")
                : Color.FromHex("#44B0D8");
        handle.DrawRect(thumb, thumbColor, filled: true);

        handle.Modulate = oldMod;
        handle.SetTransform(oldXform);
    }
}
