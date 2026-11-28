using System.Collections.Generic;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A flow-wrapping container derived from <see cref="ContentControl"/> that arranges children
/// horizontally left-to-right and wraps overflowing children onto subsequent rows.
/// Supports input relaying cleanly across remote screens.
/// </summary>
public sealed class ContentWrapContainer : ContentControl
{
    private int _horizontalSeparation = 4;
    public int HorizontalSeparation
    {
        get => _horizontalSeparation;
        set
        {
            if (_horizontalSeparation != value)
            {
                _horizontalSeparation = value;
                InvalidateMeasure();
            }
        }
    }

    private int _verticalSeparation = 4;
    public int VerticalSeparation
    {
        get => _verticalSeparation;
        set
        {
            if (_verticalSeparation != value)
            {
                _verticalSeparation = value;
                InvalidateMeasure();
            }
        }
    }

    public ContentWrapContainer()
    {
        HorizontalExpand = true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var maxWidth = float.IsFinite(availableSize.X) && availableSize.X > 0f
            ? availableSize.X
            : float.PositiveInfinity;
        var childAvail = new Vector2(maxWidth, float.PositiveInfinity);

        var curX = 0f;
        var curY = 0f;
        var rowHeight = 0f;
        var maxRowWidth = 0f;

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Measure(childAvail);
            var desired = child.DesiredSize;
            var childW = float.IsFinite(maxWidth) ? MathF.Min(desired.X, maxWidth) : desired.X;
            var childH = desired.Y;

            if (curX > 0f && curX + _horizontalSeparation + childW > maxWidth)
            {
                maxRowWidth = MathF.Max(maxRowWidth, curX);
                curY += rowHeight + _verticalSeparation;
                curX = 0f;
                rowHeight = 0f;
            }

            if (curX > 0f)
                curX += _horizontalSeparation;

            curX += childW;
            rowHeight = MathF.Max(rowHeight, childH);
        }

        maxRowWidth = MathF.Max(maxRowWidth, curX);
        var totalHeight = rowHeight > 0f ? curY + rowHeight : curY;
        return new Vector2(maxRowWidth, totalHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var maxWidth = finalSize.X > 0f ? finalSize.X : float.PositiveInfinity;
        var rows = new List<(List<(Control Child, float Width, float Height)> Items, float RowHeight)>();
        var currentRow = new List<(Control Child, float Width, float Height)>();
        var curX = 0f;
        var rowHeight = 0f;

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            var childW = float.IsFinite(maxWidth) ? MathF.Min(child.DesiredSize.X, maxWidth) : child.DesiredSize.X;
            var childH = child.DesiredSize.Y;

            if (currentRow.Count > 0 && curX + _horizontalSeparation + childW > maxWidth)
            {
                rows.Add((currentRow, rowHeight));
                currentRow = new List<(Control, float, float)>();
                curX = 0f;
                rowHeight = 0f;
            }

            if (currentRow.Count > 0)
                curX += _horizontalSeparation;

            currentRow.Add((child, childW, childH));
            curX += childW;
            rowHeight = MathF.Max(rowHeight, childH);
        }

        if (currentRow.Count > 0)
            rows.Add((currentRow, rowHeight));

        var y = 0f;
        foreach (var (items, rHeight) in rows)
        {
            var x = 0f;
            foreach (var (child, w, h) in items)
            {
                var yOffset = child.VerticalAlignment switch
                {
                    VAlignment.Top => 0f,
                    VAlignment.Bottom => MathF.Max(0f, rHeight - h),
                    VAlignment.Stretch => 0f,
                    _ => MathF.Max(0f, (rHeight - h) / 2f)
                };
                var arrangeH = child.VerticalAlignment == VAlignment.Stretch ? rHeight : h;
                child.Arrange(new UIBox2(x, y + yOffset, x + w, y + yOffset + arrangeH));
                x += w + _horizontalSeparation;
            }

            y += rHeight + _verticalSeparation;
        }

        return finalSize;
    }
}
