using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A scrollable container derived from <see cref="ContentControl"/> that supports scrolling via mouse wheel
/// and relays events cleanly in remote screen views.
/// </summary>
public class ContentScrollContainer : ContentControl
{
    public bool HScrollEnabled { get; set; } = false;
    public bool VScrollEnabled { get; set; } = true;
    public bool HScrollBarHidden { get; set; } = false;

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

    public ContentScrollContainer()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (VScrollEnabled && ChildCount > 0)
        {
            var child = GetChild(0);
            var maxScroll = Math.Max(0f, child.DesiredSize.Y - Height);
            _scrollOffsetY = Math.Clamp(_scrollOffsetY - args.Delta.Y * 30f, 0f, maxScroll);
            InvalidateArrange();
            args.Handle();
        }
        else if (HScrollEnabled && ChildCount > 0)
        {
            var child = GetChild(0);
            var maxScrollX = Math.Max(0f, child.DesiredSize.X - Width);
            var delta = args.Delta.X == 0f && !VScrollEnabled ? -args.Delta.Y : args.Delta.X;
            _scrollOffsetX = Math.Clamp(_scrollOffsetX + delta * 30f, 0f, maxScrollX);
            InvalidateArrange();
            args.Handle();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (ChildCount == 0)
            return Vector2.Zero;

        var child = GetChild(0);
        var measureAvail = new Vector2(
            HScrollEnabled ? float.PositiveInfinity : availableSize.X,
            VScrollEnabled ? float.PositiveInfinity : availableSize.Y);

        child.Measure(measureAvail);
        return Vector2.Min(child.DesiredSize, availableSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (ChildCount == 0)
            return finalSize;

        var child = GetChild(0);
        var childW = HScrollEnabled ? Math.Max(child.DesiredSize.X, finalSize.X) : finalSize.X;
        var childH = VScrollEnabled ? Math.Max(child.DesiredSize.Y, finalSize.Y) : finalSize.Y;

        var maxScrollY = Math.Max(0f, childH - finalSize.Y);
        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0f, maxScrollY);

        var maxScrollX = Math.Max(0f, childW - finalSize.X);
        _scrollOffsetX = Math.Clamp(_scrollOffsetX, 0f, maxScrollX);

        child.Arrange(new UIBox2(-_scrollOffsetX, -_scrollOffsetY, childW - _scrollOffsetX, childH - _scrollOffsetY));
        return finalSize;
    }
}
