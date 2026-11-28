using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A panel container derived from <see cref="ContentControl"/> that draws a background panel
/// and holds a child control.
/// </summary>
public class ContentPanelContainer : ContentControl
{
    public StyleBox? PanelOverride { get; set; }

    public ContentPanelContainer()
    {
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        PanelOverride?.Draw(handle, PixelSizeBox, UIScale);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var styleSize = PanelOverride?.MinimumSize ?? Vector2.Zero;
        var measureSize = Vector2.Max(availableSize - styleSize, Vector2.Zero);
        var childSize = Vector2.Zero;

        foreach (var child in Children)
        {
            child.Measure(measureSize);
            childSize = Vector2.Max(childSize, child.DesiredSize);
        }

        return styleSize + childSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var ourSize = UIBox2.FromDimensions(Vector2.Zero, finalSize);
        var contentBox = PanelOverride?.GetContentBox(ourSize, 1) ?? ourSize;

        foreach (var child in Children)
        {
            child.Arrange(contentBox);
        }

        return finalSize;
    }
}
