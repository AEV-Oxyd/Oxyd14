using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A lightweight texture display control derived from <see cref="ContentControl"/> so it
/// stays fully compatible with <see cref="IInputRelayTarget"/> and remote screen relaying.
/// </summary>
public sealed class ContentTextureRect : ContentControl
{
    private Texture? _texture;
    private Vector2 _displaySize = new(16, 16);

    public Texture? Texture
    {
        get => _texture;
        set
        {
            if (_texture != value)
            {
                _texture = value;
                InvalidateMeasure();
            }
        }
    }

    public Vector2 DisplaySize
    {
        get => _displaySize;
        set
        {
            if (_displaySize != value)
            {
                _displaySize = value;
                InvalidateMeasure();
            }
        }
    }

    public Color Modulate { get; set; } = Color.White;

    public ContentTextureRect()
    {
        MouseFilter = MouseFilterMode.Ignore;
        VerticalAlignment = VAlignment.Center;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return _texture != null ? _displaySize : Vector2.Zero;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_texture == null)
            return;

        var drawW = MathF.Min(PixelWidth, _displaySize.X * UIScale);
        var drawH = MathF.Min(PixelHeight, _displaySize.Y * UIScale);
        if (drawW <= 0f || drawH <= 0f)
            return;

        var x = (PixelWidth - drawW) / 2f;
        var y = (PixelHeight - drawH) / 2f;
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(new Vector2(x, y), new Vector2(drawW, drawH)), Modulate);
    }
}
