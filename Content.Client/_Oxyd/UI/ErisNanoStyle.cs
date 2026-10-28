using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.UI;

/// <summary>
/// Shared recreation of the CEV-Eris NanoUI dark skin (nano/css/shared.css) for Eris-ported
/// machine/book windows. Everything here is opt-in: these controls only style themselves and
/// never touch global theme state, so non-Eris UI is unaffected.
///
/// Convention (see <c>Content.Client/_Oxyd/Medical/UI</c> on eris-moebius-port for the
/// reference implementation): a window recreates its .tmpl by making the XAML a compact
/// <c>FancyWindow</c> containing one <see cref="ErisWindowBody"/> panel, then builds its
/// content in code-behind with the controls below — gold <see cref="ErisItemLabel"/>s for
/// field names, <see cref="ErisSpecRow"/> for label/value lines, <see cref="ErisLink"/> pill
/// buttons for actions, <see cref="ErisBar"/> for displayBars, <see cref="ErisStatusBox"/>
/// for inset text blocks, and <see cref="ErisCandystripe"/> for list rows.
///
/// Do NOT use <c>ui:OxTag</c>/<c>InitTextureEris</c> inside windows — that tiled texture is
/// the in-game HUD frame skin, not the NanoUI window skin.
/// </summary>
internal static class ErisNanoColors
{
    // window body: flat near-black NanoUI background
    public static readonly Color BodyBg = new Color(0.15f, 0.15f, 0.15f);
    // .display card: translucent dark panel
    public static readonly Color CardBg = new Color(0f, 0f, 0f, 0.45f);
    public static readonly Color CardBorder = Color.FromHex("#40628a");
    // meatMode accents kept for ports that need them (surgery, organic machines)
    public static readonly Color MeatBg = Color.FromHex("#7e050c");
    public static readonly Color WoundHeader = Color.FromHex("#a0222a");
    // text
    public static readonly Color Text = Color.White;
    public static readonly Color LabelText = Color.FromHex("#8ba5c4");
    public static readonly Color ItemLabelText = Color.FromHex("#e9c183");
    public static readonly Color GoodText = Color.FromHex("#008000");
    public static readonly Color BadText = Color.FromHex("#b00e0e");
    public static readonly Color AccentText = Color.FromHex("#ff6aa0");
    public static readonly Color WarningText = Color.FromHex("#ffa500");
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

/// <summary>Window body panel: the flat NanoUI near-black background every Eris window
/// starts from. Put this as the single root child of a compact FancyWindow.</summary>
internal sealed class ErisWindowBody : PanelContainer
{
    public ErisWindowBody()
    {
        PanelOverride = new StyleBoxFlat(ErisNanoColors.BodyBg);
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 4);
        VerticalExpand = true;
        HorizontalExpand = true;
    }
}

/// <summary>Eris .display card: translucent black panel with padding.</summary>
internal sealed class ErisPanel : PanelContainer
{
    public ErisPanel()
    {
        PanelOverride = new StyleBoxFlat(ErisNanoColors.CardBg);
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 6);
    }
}

/// <summary>Eris .nanoMap card: rgba(0,0,0,.85) tile framed by a #40628a border
/// (nanomapBackground.png is a solid near-black tile, so a flat box is identical).</summary>
internal sealed class ErisNanoMapPanel : PanelContainer
{
    public ErisNanoMapPanel()
    {
        PanelOverride = new StyleBoxFlat(new Color(0f, 0f, 0f, 0.85f))
        {
            BorderColor = ErisNanoColors.CardBorder,
            BorderThickness = new Thickness(1),
        };
        PanelOverride.SetContentMarginOverride(StyleBox.Margin.All, 4);
    }
}

/// <summary>Eris helper.link: blue pill button, white text, dark border; redButton variant
/// for destructive actions; greyed out when disabled like a disabled NanoUI link.</summary>
internal sealed class ErisLink : ContainerButton
{
    private readonly Label _label;

    /// <summary>XAML-instantiable ctor; set <see cref="Text"/> afterwards.</summary>
    public ErisLink() : this(string.Empty)
    {
    }

    public ErisLink(string text, bool red = false)
    {
        var bg = red ? ErisNanoColors.RedButtonBg : ErisNanoColors.LinkBg;
        StyleBoxOverride = new StyleBoxFlat(bg)
        {
            BorderColor = ErisNanoColors.LinkBorder,
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
        StyleBoxOverride = new StyleBoxFlat(red ? ErisNanoColors.RedButtonBg : ErisNanoColors.LinkBg)
        {
            BorderColor = ErisNanoColors.LinkBorder,
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
        StyleBoxOverride = new StyleBoxFlat(ErisNanoColors.LinkDisabledBg)
        {
            BorderColor = ErisNanoColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = ErisNanoColors.LinkDisabledText;
    }

    /// <summary>Render as the lit member of a selected pair (Eris .selected link —
    /// lighter blue box, white border text highlight).</summary>
    public void SetSelectedLook()
    {
        Disabled = false;
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
            BorderColor = ErisNanoColors.LinkBorder,
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
    private Color _fill = ErisNanoColors.BarDefault;
    private static readonly StyleBoxFlat Trough = new(ErisNanoColors.BarBg)
    {
        BorderColor = ErisNanoColors.BarBorder,
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
        FontColorOverride = ErisNanoColors.ItemLabelText;
    }
}

/// <summary>Muted secondary label (Eris .itemContent / helper.comment text).</summary>
internal sealed class ErisSubLabel : Label
{
    public ErisSubLabel(string text)
    {
        Text = text;
        FontColorOverride = ErisNanoColors.LabelText;
    }
}

/// <summary>Eris .statusDisplay: dark inset box used for status text blocks.</summary>
internal sealed class ErisStatusBox : PanelContainer
{
    public ErisStatusBox()
    {
        PanelOverride = new StyleBoxFlat(new Color(0.08f, 0.08f, 0.08f, 0.9f))
        {
            BorderColor = ErisNanoColors.CardBorder,
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

/// <summary>NanoUI list-row button (ritual_book.tmpl entries): flat dark cell with a blue
/// border, lit lighter when the row is selected, greyed text when the entry is unavailable.
/// Supports ToggleMode/Group like a radio list — set <see cref="Pressed"/> then
/// <see cref="RefreshLook"/> after changing either flag.</summary>
internal sealed class ErisRowButton : ContainerButton
{
    private readonly Label _label;

    public ErisRowButton()
    {
        _label = new Label
        {
            FontColorOverride = Color.White,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_label);
        RefreshLook();
        OnMouseEntered += _ => ModulateSelfOverride = new Color(1.2f, 1.2f, 1.2f);
        OnMouseExited += _ => ModulateSelfOverride = null;
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public bool Available { get; set; } = true;

    /// <summary>Re-apply the look for the current <see cref="ContainerButton.Pressed"/> and
    /// <see cref="Available"/> state — selected rows get the lighter .selected-link body.</summary>
    public void RefreshLook()
    {
        StyleBoxOverride = new StyleBoxFlat(Pressed ? Color.FromHex("#5b7ea4") : Color.FromHex("#27354a"))
        {
            BorderColor = Pressed ? Color.FromHex("#a0c0e0") : ErisNanoColors.LinkBorder,
            BorderThickness = new Thickness(1),
        };
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        StyleBoxOverride.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        _label.FontColorOverride = Available ? Color.White : ErisNanoColors.LinkDisabledText;
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
