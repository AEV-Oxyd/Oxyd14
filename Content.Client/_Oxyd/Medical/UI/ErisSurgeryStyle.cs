using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>
/// Re-creates the CEV-Eris NanoUI surgery skin (nano/css/shared.css + meatMode()):
/// blood-red body with the splatter texture, translucent dark .display cards,
/// #40628a accents, gold labels, and the good/average/bad displayBar palette.
/// </summary>
internal static class ErisSurgeryColors
{
    // body background (meatMode)
    public static readonly Color MeatBg = Color.FromHex("#7e050c");
    // .display card: rgba(0,0,0,0.33) with a subtle inset feel
    public static readonly Color CardBg = new Color(0, 0, 0, 0.45f);
    public static readonly Color CardBorder = Color.FromHex("#40628a");
    // organ header underline when a fleshy organ is wounded/open
    public static readonly Color WoundHeader = Color.FromHex("#a0222a");
    // text
    public static readonly Color Text = Color.White;
    public static readonly Color LabelText = Color.FromHex("#8ba5c4");
    public static readonly Color ItemLabelText = Color.FromHex("#e9c183");
    public static readonly Color OrganTypeText = Color.FromHex("#ff6aa0");
    public static readonly Color WoundCountText = Color.FromHex("#ffa500");
    public static readonly Color GoodText = Color.FromHex("#008000");
    public static readonly Color BadText = Color.FromHex("#b00e0e");
    // helper.link / .button
    public static readonly Color LinkBg = Color.FromHex("#40628a");
    public static readonly Color LinkBorder = Color.FromHex("#161616");
    public static readonly Color LinkDisabledBg = Color.FromHex("#2a2f36");
    public static readonly Color LinkDisabledText = Color.FromHex("#999999");
    public static readonly Color RedButtonBg = Color.FromHex("#b00e0e");
    // displayBar
    public static readonly Color BarBg = Color.FromHex("#272727");
    public static readonly Color BarBorder = Color.FromHex("#40628a");
    public static readonly Color BarDefault = Color.FromHex("#40628a");
    public static readonly Color BarGood = Color.FromHex("#4f7529");
    public static readonly Color BarAverage = Color.FromHex("#cd6500");
    public static readonly Color BarBad = Color.FromHex("#b00e0e");
}

/// <summary>Eris .display card: translucent black panel with 4px padding.</summary>
internal sealed class ErisPanel : PanelContainer
{
    public ErisPanel()
    {
        PanelOverride = new StyleBoxFlat(ErisSurgeryColors.CardBg);
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 6);
    }
}

/// <summary>Eris .nanoMap card: rgba(0,0,0,.85) tile framed by a #40628a border
/// (nanomapBackground.png is a solid near-black tile, so a flat box is identical).</summary>
internal sealed class ErisNanoMapPanel : PanelContainer
{
    public ErisNanoMapPanel()
    {
        PanelOverride = new StyleBoxFlat(new Color(0, 0, 0, 0.85f))
        {
            BorderColor = ErisSurgeryColors.CardBorder,
            BorderThickness = new Thickness(1),
        };
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 4);
    }
}

/// <summary>Eris helper.link: blue pill button, white text, dark border; redButton variant
/// for amputate; greyed out when disabled like a disabled NanoUI link.</summary>
internal sealed class ErisLink : ContainerButton
{
    private readonly Label _label;

    public ErisLink(string text, bool red = false)
    {
        var bg = red ? ErisSurgeryColors.RedButtonBg : ErisSurgeryColors.LinkBg;
        StyleBoxOverride = new StyleBoxFlat(bg)
        {
            BorderColor = ErisSurgeryColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label = new Label
        {
            Text = text,
            FontColorOverride = Color.White,
            MouseFilter = MouseFilterMode.Ignore,
            HorizontalAlignment = HAlignment.Center,
        };
        AddChild(_label);
        OnMouseEntered += _ => ModulateSelfOverride = new Color(1.2f, 1.2f, 1.2f);
        OnMouseExited += _ => ModulateSelfOverride = null;
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    /// <summary>Restore the normal blue pill look (after SetDisabledLook/SetSelectedLook).</summary>
    public void SetEnabledLook(bool red = false)
    {
        Disabled = false;
        StyleBoxOverride = new StyleBoxFlat(red ? ErisSurgeryColors.RedButtonBg : ErisSurgeryColors.LinkBg)
        {
            BorderColor = ErisSurgeryColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = Color.White;
    }

    /// <summary>Render as a disabled Eris link (grey text, darker box, no click).</summary>
    public void SetDisabledLook()
    {
        Disabled = true;
        StyleBoxOverride = new StyleBoxFlat(ErisSurgeryColors.LinkDisabledBg)
        {
            BorderColor = ErisSurgeryColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = ErisSurgeryColors.LinkDisabledText;
    }

    /// <summary>Render as the lit member of a selected pair (Eris .selected link —
    /// lighter blue box, white border text highlight).</summary>
    public void SetSelectedLook()
    {
        StyleBoxOverride = new StyleBoxFlat(Color.FromHex("#5b7ea4"))
        {
            BorderColor = Color.FromHex("#a0c0e0"),
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = Color.White;
    }

    /// <summary>Render as the un-selected member of a pair (dim blue box).</summary>
    public void SetUnselectedLook()
    {
        Disabled = false;
        StyleBoxOverride = new StyleBoxFlat(Color.FromHex("#27354a"))
        {
            BorderColor = ErisSurgeryColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = Color.FromHex("#9db6cc");
    }
}

/// <summary>Eris helper.displayBar: 16px bar, #272727 trough with a #40628a border,
/// fill width proportional to value and tinted by the good/average/bad class.</summary>
internal sealed class ErisBar : Control
{
    private readonly Label _label;
    private float _fraction;
    private Color _fill = ErisSurgeryColors.BarDefault;
    private static readonly StyleBoxFlat Trough = new(ErisSurgeryColors.BarBg)
    {
        BorderColor = ErisSurgeryColors.BarBorder,
        BorderThickness = new Thickness(1),
    };

    public ErisBar()
    {
        MinHeight = 14;
        _label = new Label
        {
            FontColorOverride = Color.White,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_label);
    }

    public void SetValue(float fraction, Color fill, string text)
    {
        _fraction = Math.Clamp(fraction, 0f, 1f);
        _fill = fill;
        _label.Text = text;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = new Vector2(PixelWidth, PixelHeight);
        Trough.Draw(handle, new UIBox2(PixelPosition, PixelPosition + size), 1f);
        var fillWidth = size.X * _fraction;
        if (fillWidth > 0)
        {
            handle.DrawRect(new UIBox2(PixelPosition + new Vector2(1, 1),
                PixelPosition + new Vector2(1 + fillWidth, size.Y - 1)), _fill);
        }
    }
}

/// <summary>Eris .itemLabel: gold #e9c183 label cell used for spec/stat rows
/// ("Critical Health:", "Type:", ...).</summary>
internal sealed class ErisItemLabel : Label
{
    public ErisItemLabel(string text)
    {
        Text = text;
        FontColorOverride = ErisSurgeryColors.ItemLabelText;
    }
}

/// <summary>Muted secondary label (Eris .itemContent / helper.comment text).</summary>
internal sealed class ErisSubLabel : Label
{
    public ErisSubLabel(string text)
    {
        Text = text;
        FontColorOverride = ErisSurgeryColors.LabelText;
    }
}

/// <summary>Eris .statusDisplay: dark inset box used for beaker contents and
/// status text blocks ("No beaker loaded", cell status, entry spec blocks).</summary>
internal sealed class ErisStatusBox : PanelContainer
{
    public ErisStatusBox()
    {
        PanelOverride = new StyleBoxFlat(new Color(0.08f, 0.08f, 0.08f, 0.9f))
        {
            BorderColor = ErisSurgeryColors.CardBorder,
            BorderThickness = new Thickness(1),
        };
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 6);
    }
}

/// <summary>Eris .candystripe row: alternating translucent-dark row background
/// for list tables (index 0 = dark, index 1 = darker).</summary>
internal static class ErisCandystripe
{
    public static PanelContainer Wrap(Control row, int index)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(index % 2 == 0
                ? new Color(0.10f, 0.10f, 0.10f, 0.85f)
                : new Color(0.05f, 0.05f, 0.05f, 0.85f)),
            HorizontalExpand = true,
        };
        panel.AddChild(row);
        return panel;
    }
}

/// <summary>Eris spec row: itemLabel cell (fixed width) + value cell.</summary>
internal sealed class ErisSpecRow : BoxContainer
{
    private readonly BoxContainer _valueSlot;

    public ErisSpecRow(string label, float labelWidth = 120f)
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        var l = new ErisItemLabel(label);
        l.SetWidth = labelWidth;
        l.ClipText = true;
        AddChild(l);
        _valueSlot = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };
        AddChild(_valueSlot);
    }

    public void AddValue(Control control) => _valueSlot.AddChild(control);
}
