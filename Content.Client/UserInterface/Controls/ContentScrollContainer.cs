using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A scrollable container derived from <see cref="ContentControl"/> that supports scrolling via mouse wheel
/// and scrollbar dragging, clearly denotes scrollable wells with a textured recessed background and scrollbar,
/// and relays events cleanly in remote screen views.
/// </summary>
public class ContentScrollContainer : ContentControl
{
    private const float ScrollBarLogicalWidth = 10f;
    private const float InsetPadding = 3f;

    public bool HScrollEnabled { get; set; } = false;
    public bool VScrollEnabled { get; set; } = true;
    public bool HScrollBarHidden { get; set; } = false;

    /// <summary>
    /// When true, renders a subtle recessed textured background, border, scrollbar track/thumb,
    /// and top/bottom overflow indicator strips whenever content can scroll (or always if <see cref="AlwaysShowBackground"/>).
    /// </summary>
    public bool ShowScrollVisuals { get; set; } = true;

    /// <summary>
    /// When true, renders the recessed scrollable well texture/border even when content fits without scrolling.
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

    public bool IsVerticallyScrollable => VScrollEnabled && _lastMaxScrollY > 0.5f;
    public bool IsHorizontallyScrollable => HScrollEnabled && _lastMaxScrollX > 0.5f;
    public float MaxScrollY => _lastMaxScrollY;

    public ContentScrollContainer()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
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

        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick && _draggingVThumb)
        {
            _draggingVThumb = false;
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!ShowScrollVisuals || !IsVerticallyScrollable)
        {
            _hoveringVBar = false;
            return;
        }

        var track = GetVerticalTrackPixelRect();
        _hoveringVBar = track.Contains(args.RelativePixelPosition);

        if (_draggingVThumb)
        {
            var thumb = GetVerticalThumbPixelRect(track);
            var usableTrack = MathF.Max(1f, track.Height - thumb.Height);
            var deltaY = args.RelativePixelPosition.Y - _dragStartPixelY;
            var scrollDelta = (deltaY / usableTrack) * _lastMaxScrollY;
            _scrollOffsetY = Math.Clamp(_dragStartScrollY + scrollDelta, 0f, _lastMaxScrollY);
            InvalidateArrange();
        }
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoveringVBar = false;
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
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!ShowScrollVisuals)
            return;

        var canScrollV = IsVerticallyScrollable;
        var canScrollH = IsHorizontallyScrollable;
        if (!AlwaysShowBackground && !canScrollV && !canScrollH)
            return;

        var rect = PixelSizeBox;

        // Recessed dark scrollable well background
        handle.DrawRect(rect, Color.FromHex("#060D15"), filled: true);

        // Subtle recessed diagonal/horizontal striped texture to clearly distinguish scrollable regions
        var stripeStep = MathF.Max(8f, 10f * UIScale);
        var stripeH = MathF.Max(1f, 2f * UIScale);
        var stripeColor = new Color(0.14f, 0.36f, 0.50f, 0.10f);
        var scrollPhase = (_scrollOffsetY * UIScale) % stripeStep;
        for (var y = -scrollPhase; y < PixelHeight; y += stripeStep)
        {
            var top = MathF.Max(0f, y);
            var bot = MathF.Min(PixelHeight, y + stripeH);
            if (bot > top)
                handle.DrawRect(new UIBox2(1f, top, MathF.Max(1f, PixelWidth - 1f), bot), stripeColor, filled: true);
        }

        // Inset frame border (brighter cyan tint when actively scrollable)
        var borderColor = canScrollV || canScrollH
            ? Color.FromHex("#225673")
            : Color.FromHex("#143245");
        handle.DrawRect(rect, borderColor, filled: false);

        if (!canScrollV)
            return;

        // Top & bottom overflow edge accent bars showing more content exists above/below
        var edgeH = MathF.Max(2f, 2f * UIScale);
        var barWReserve = (ScrollBarLogicalWidth + InsetPadding) * UIScale;
        var edgeRight = MathF.Max(2f, PixelWidth - barWReserve);
        if (_scrollOffsetY > 0.5f)
        {
            handle.DrawRect(new UIBox2(2f, 1f, edgeRight, 1f + edgeH), new Color(0f, 0.83f, 1f, 0.65f), filled: true);
        }

        if (_scrollOffsetY < _lastMaxScrollY - 0.5f)
        {
            handle.DrawRect(new UIBox2(2f, PixelHeight - 1f - edgeH, edgeRight, PixelHeight - 1f), new Color(0f, 0.83f, 1f, 0.65f), filled: true);
        }

        // Right-edge scrollbar track & proportional thumb
        var track = GetVerticalTrackPixelRect();
        handle.DrawRect(track, Color.FromHex("#0B1A26"), filled: true);
        handle.DrawRect(track, Color.FromHex("#1E4C66"), filled: false);

        var thumb = GetVerticalThumbPixelRect(track);
        var thumbColor = _draggingVThumb
            ? Color.FromHex("#66DCFF")
            : _hoveringVBar
                ? Color.FromHex("#45BCE6")
                : Color.FromHex("#2B87AD");
        handle.DrawRect(thumb, thumbColor, filled: true);
    }
}
