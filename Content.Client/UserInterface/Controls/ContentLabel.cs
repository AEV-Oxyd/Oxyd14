using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A label GUI control that derives from <see cref="ContentControl"/> to support input relaying
/// and clean rendering across remote screen streams.
/// </summary>
public class ContentLabel : ContentControl
{
    private string? _text;
    public string? Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                InvalidateMeasure();
            }
        }
    }

    public Color? FontColorOverride { get; set; }
    public Font? FontOverride { get; set; }
    public bool ClipText { get; set; }

    public ContentLabel()
    {
        MouseFilter = MouseFilterMode.Ignore;
        VerticalAlignment = VAlignment.Center;
    }

    public ContentLabel(string text) : this()
    {
        Text = text;
    }

    private Font GetFont()
    {
        if (FontOverride != null)
            return FontOverride;

        if (TryGetStyleProperty<Font>("font", out var styleFont) && styleFont != null)
            return styleFont;

        return UserInterfaceManager.ThemeDefaults.DefaultFont;
    }

    private Color GetFontColor()
    {
        if (FontColorOverride != null)
            return FontColorOverride.Value;

        if (TryGetStyleProperty<Color>("font-color", out var styleColor))
            return styleColor;

        return Color.White;
    }

    private Vector2 MeasureString(Font font, string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
            {
                width += metrics.Advance;
            }
        }

        return new Vector2(width, font.GetHeight(UIScale));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return Vector2.Zero;

        var font = GetFont();
        var dim = MeasureString(font, Text);
        return new Vector2(dim.X / UIScale, dim.Y / UIScale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (string.IsNullOrEmpty(Text))
            return;

        var font = GetFont();
        var color = GetFontColor();
        var dim = MeasureString(font, Text);

        var posX = HorizontalAlignment switch
        {
            HAlignment.Center => Math.Max(0f, (PixelWidth - dim.X) / 2f),
            HAlignment.Right => Math.Max(0f, PixelWidth - dim.X),
            _ => 0f
        };

        var posY = Math.Max(0f, (PixelHeight - dim.Y) / 2f);

        handle.DrawString(font, new Vector2(posX, posY), Text, UIScale, color);
    }
}
