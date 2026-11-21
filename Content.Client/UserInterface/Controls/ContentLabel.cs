using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A label GUI control that derives from <see cref="ContentControl"/> to support input relaying,
/// automatic word wrapping within container bounds, and clean rendering across remote screen streams.
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

    private bool _wordWrap = true;
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap != value)
            {
                _wordWrap = value;
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

    private float GetRuneAdvance(Font font, Rune rune)
    {
        try
        {
            if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
                return metrics.Advance;
        }
        catch (Exception)
        {
            // Skip glyphs whose fallback font format cannot be rasterized by FreeType.
        }

        return 0f;
    }

    private List<(string Text, float Width)> BuildWrappedLines(Font font, string text, float maxPixelWidth, out float maxLineWidth)
    {
        var result = new List<(string Text, float Width)>();
        var maxW = 0f;

        var wrapActive = _wordWrap && float.IsFinite(maxPixelWidth) && maxPixelWidth > 1f;
        var current = new StringBuilder();
        var currentWidth = 0f;

        var lastBreakIdx = -1;
        var widthAtLastBreak = 0f;

        void FlushLine(string lineText, float lineWidth)
        {
            result.Add((lineText, lineWidth));
            if (lineWidth > maxW)
                maxW = lineWidth;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r')
                continue;

            if (rune.Value == '\n')
            {
                FlushLine(current.ToString(), currentWidth);
                current.Clear();
                currentWidth = 0f;
                lastBreakIdx = -1;
                widthAtLastBreak = 0f;
                continue;
            }

            var adv = GetRuneAdvance(font, rune);
            var isSpace = rune.Value == ' ' || rune.Value == '\t';
            var isBreakAfter = rune.Value is ',' or '/' or '|' or ';' or '-';

            if (wrapActive && current.Length > 0 && currentWidth + adv > maxPixelWidth)
            {
                if (isSpace)
                {
                    FlushLine(current.ToString().TrimEnd(), currentWidth);
                    current.Clear();
                    currentWidth = 0f;
                    lastBreakIdx = -1;
                    widthAtLastBreak = 0f;
                    continue;
                }

                if (lastBreakIdx > 0)
                {
                    var fullStr = current.ToString();
                    var head = fullStr[..lastBreakIdx].TrimEnd();
                    var tail = fullStr[lastBreakIdx..].TrimStart();
                    FlushLine(head, widthAtLastBreak);

                    current.Clear();
                    current.Append(tail);
                    currentWidth = MeasureSingleLine(font, tail);
                    lastBreakIdx = -1;
                    widthAtLastBreak = 0f;

                    // If tail + new rune still overflows on its own line, fall through to hard wrap.
                    if (current.Length > 0 && currentWidth + adv > maxPixelWidth)
                    {
                        FlushLine(current.ToString(), currentWidth);
                        current.Clear();
                        currentWidth = 0f;
                    }
                }
                else
                {
                    FlushLine(current.ToString(), currentWidth);
                    current.Clear();
                    currentWidth = 0f;
                }
            }

            if (current.Length == 0 && isSpace && result.Count > 0)
            {
                // Avoid stray leading spaces on wrapped continuation lines while preserving indentation on explicit newlines.
            }

            current.Append(rune.ToString());
            currentWidth += adv;

            if (isSpace || isBreakAfter)
            {
                lastBreakIdx = current.Length;
                widthAtLastBreak = currentWidth;
            }
        }

        FlushLine(current.ToString(), currentWidth);
        maxLineWidth = maxW;
        return result;
    }

    private float MeasureSingleLine(Font font, string text)
    {
        var w = 0f;
        foreach (var rune in text.EnumerateRunes())
            w += GetRuneAdvance(font, rune);
        return w;
    }

    private float ComputeTotalHeight(Font font, int lineCount)
    {
        if (lineCount <= 0)
            return 0f;

        return lineCount == 1
            ? font.GetHeight(UIScale)
            : (lineCount - 1) * font.GetLineHeight(UIScale) + font.GetHeight(UIScale);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return Vector2.Zero;

        var font = GetFont();
        var maxPixelW = _wordWrap && float.IsFinite(availableSize.X) && availableSize.X > 0f
            ? availableSize.X * UIScale
            : float.PositiveInfinity;

        var lines = BuildWrappedLines(font, Text, maxPixelW, out var maxLineWidth);
        var height = ComputeTotalHeight(font, lines.Count) / UIScale;
        var width = ClipText ? 0f : maxLineWidth / UIScale;

        if (_wordWrap && float.IsFinite(availableSize.X) && availableSize.X > 0f)
            width = MathF.Min(width, availableSize.X);

        return new Vector2(width, height);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        RectClipContent = ClipText || _wordWrap;
        base.Draw(handle);

        if (string.IsNullOrEmpty(Text))
            return;

        var font = GetFont();
        var color = GetFontColor();
        var maxPixelW = _wordWrap && PixelWidth > 1
            ? (float) PixelWidth
            : float.PositiveInfinity;

        var lines = BuildWrappedLines(font, Text, maxPixelW, out _);
        var totalHeight = ComputeTotalHeight(font, lines.Count);
        var lineHeight = font.GetLineHeight(UIScale);

        var startY = VerticalAlignment switch
        {
            VAlignment.Top => 0f,
            VAlignment.Bottom => Math.Max(0f, PixelHeight - totalHeight),
            _ => Math.Max(0f, (PixelHeight - totalHeight) / 2f)
        };

        for (var i = 0; i < lines.Count; i++)
        {
            var (lineText, lineWidth) = lines[i];
            if (lineText.Length == 0)
                continue;

            var posX = HorizontalAlignment switch
            {
                HAlignment.Center => Math.Max(0f, (PixelWidth - lineWidth) / 2f),
                HAlignment.Right => Math.Max(0f, PixelWidth - lineWidth),
                _ => 0f
            };

            handle.DrawString(font, new Vector2(posX, startY + i * lineHeight), lineText, UIScale, color);
        }
    }
}
