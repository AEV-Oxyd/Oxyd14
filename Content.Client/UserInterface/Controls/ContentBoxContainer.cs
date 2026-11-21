using System.Collections.Generic;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A container that arranges its children sequentially (either horizontally or vertically),
/// deriving from <see cref="ContentControl"/> so that inputs can be properly relayed to it
/// and its children across remote screens.
/// </summary>
public class ContentBoxContainer : ContentControl
{
    public enum LayoutOrientation : byte
    {
        Horizontal = 0,
        Vertical = 1,
    }

    public enum AlignMode : byte
    {
        Begin = 0,
        Center = 1,
        End = 2,
    }

    private LayoutOrientation _orientation = LayoutOrientation.Horizontal;
    public LayoutOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                InvalidateMeasure();
            }
        }
    }

    private int? _separationOverride;
    public int? SeparationOverride
    {
        get => _separationOverride;
        set
        {
            if (_separationOverride != value)
            {
                _separationOverride = value;
                InvalidateMeasure();
            }
        }
    }

    public AlignMode Align { get; set; } = AlignMode.Begin;

    public int ActualSeparation => SeparationOverride ?? 0;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var separation = ActualSeparation;
        var visibleCount = 0;
        foreach (var child in Children)
        {
            if (child.Visible)
                visibleCount++;
        }

        var totalSeparation = Math.Max(0, visibleCount - 1) * separation;
        var width = 0f;
        var height = 0f;

        if (Orientation == LayoutOrientation.Horizontal)
        {
            width += totalSeparation;
            var fixedWidth = 0f;
            var totalStretch = 0f;
            var expandChildren = new List<Control>();

            // Pass 1: Measure non-expanding children (e.g. action buttons) first so expanding labels
            // can wrap cleanly within the remaining horizontal space without pushing buttons off-screen.
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                if (child.HorizontalExpand)
                {
                    expandChildren.Add(child);
                    totalStretch += Math.Max(0.001f, child.SizeFlagsStretchRatio);
                    continue;
                }

                child.Measure(availableSize);
                fixedWidth += child.DesiredSize.X;
                height = Math.Max(height, child.DesiredSize.Y);
            }

            width += fixedWidth;

            if (expandChildren.Count > 0)
            {
                var remainingX = float.IsFinite(availableSize.X)
                    ? MathF.Max(0f, availableSize.X - fixedWidth - totalSeparation)
                    : availableSize.X;

                foreach (var child in expandChildren)
                {
                    var share = float.IsFinite(remainingX)
                        ? remainingX * (Math.Max(0.001f, child.SizeFlagsStretchRatio) / totalStretch)
                        : float.PositiveInfinity;
                    child.Measure(new Vector2(share, availableSize.Y));
                    width += child.DesiredSize.X;
                    height = Math.Max(height, child.DesiredSize.Y);
                }
            }
        }
        else
        {
            height += totalSeparation;
            foreach (var child in Children)
            {
                if (!child.Visible)
                    continue;

                child.Measure(availableSize);
                height += child.DesiredSize.Y;
                width = Math.Max(width, child.DesiredSize.X);
            }
        }

        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var separation = ActualSeparation;
        var visibleChildren = new List<Control>();
        var totalStretch = 0f;
        var nonStretchSize = 0f;

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            visibleChildren.Add(child);
            var stretch = Orientation == LayoutOrientation.Horizontal ? child.HorizontalExpand : child.VerticalExpand;
            if (stretch)
                totalStretch += Math.Max(0.001f, child.SizeFlagsStretchRatio);
            else
                nonStretchSize += Orientation == LayoutOrientation.Horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
        }

        var totalSeparation = Math.Max(0, visibleChildren.Count - 1) * separation;
        var availForStretch = Math.Max(0, (Orientation == LayoutOrientation.Horizontal ? finalSize.X : finalSize.Y) - nonStretchSize - totalSeparation);

        var offset = 0f;
        foreach (var child in visibleChildren)
        {
            var stretch = Orientation == LayoutOrientation.Horizontal ? child.HorizontalExpand : child.VerticalExpand;
            float mainSize;
            if (stretch && totalStretch > 0)
            {
                mainSize = availForStretch * (Math.Max(0.001f, child.SizeFlagsStretchRatio) / totalStretch);
            }
            else
            {
                mainSize = Orientation == LayoutOrientation.Horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            }

            if (Orientation == LayoutOrientation.Horizontal)
            {
                var crossSize = child.VerticalAlignment == VAlignment.Stretch ? finalSize.Y : Math.Min(child.DesiredSize.Y, finalSize.Y);
                var crossOffset = child.VerticalAlignment switch
                {
                    VAlignment.Center => (finalSize.Y - crossSize) / 2f,
                    VAlignment.Bottom => finalSize.Y - crossSize,
                    _ => 0f
                };

                child.Arrange(new UIBox2(offset, crossOffset, offset + mainSize, crossOffset + crossSize));
            }
            else
            {
                var crossSize = child.HorizontalAlignment == HAlignment.Stretch ? finalSize.X : Math.Min(child.DesiredSize.X, finalSize.X);
                var crossOffset = child.HorizontalAlignment switch
                {
                    HAlignment.Center => (finalSize.X - crossSize) / 2f,
                    HAlignment.Right => finalSize.X - crossSize,
                    _ => 0f
                };

                child.Arrange(new UIBox2(crossOffset, offset, crossOffset + crossSize, offset + mainSize));
            }

            offset += mainSize + separation;
        }

        return finalSize;
    }
}
