using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A button control derived from <see cref="ContentControl"/> that properly receives and relays inputs
/// (clicks, hovers, releases) across remote screen viewers.
/// </summary>
public class ContentButton : ContentControl
{
    public const string StyleClassButton = "Button";

    public enum ActionMode : byte
    {
        Press = 0,
        Release = 1,
    }

    public class ButtonEventArgs : EventArgs
    {
        public ContentButton Button { get; }
        public GUIBoundKeyEventArgs? Event { get; }

        public ButtonEventArgs(ContentButton button, GUIBoundKeyEventArgs? args)
        {
            Button = button;
            Event = args;
        }
    }

    public sealed class ButtonToggledEventArgs : ButtonEventArgs
    {
        public bool Pressed { get; }

        public ButtonToggledEventArgs(bool pressed, ContentButton button, GUIBoundKeyEventArgs? args) : base(button, args)
        {
            Pressed = pressed;
        }
    }

    private readonly ContentLabel _label;
    private Texture? _icon;
    private Vector2 _iconSize = new(16, 16);
    private Color? _iconModulate;
    private bool _disabled;
    private bool _pressed;
    private bool _isBeingHovered;
    private bool _isPressing;

    public ContentLabel Label => _label;

    public string? Text
    {
        get => _label.Text;
        set
        {
            if (_label.Text != value)
            {
                _label.Text = value;
                InvalidateMeasure();
            }
        }
    }

    public Texture? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                InvalidateMeasure();
            }
        }
    }

    public Vector2 IconSize
    {
        get => _iconSize;
        set
        {
            if (_iconSize != value)
            {
                _iconSize = value;
                InvalidateMeasure();
            }
        }
    }

    public Color? IconModulate
    {
        get => _iconModulate;
        set => _iconModulate = value;
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            if (_disabled != value)
            {
                _disabled = value;
            }
        }
    }

    public bool ToggleMode { get; set; }

    /// <summary>
    /// When true, renders without an outer border or default opaque fill so the button can sit
    /// seamlessly inside a shared container pill (e.g., an OS tab with inline close button).
    /// </summary>
    public bool Borderless { get; set; }

    /// <summary>
    /// Optional hover/press tint override when <see cref="Borderless"/> is enabled.
    /// </summary>
    public Color? HoverColorOverride { get; set; }

    public bool Pressed
    {
        get => _pressed;
        set
        {
            if (_pressed != value)
            {
                _pressed = value;
            }
        }
    }

    public ActionMode Mode { get; set; } = ActionMode.Release;

    public event Action<ButtonEventArgs>? OnPressed;
    public event Action<ButtonEventArgs>? OnButtonDown;
    public event Action<ButtonEventArgs>? OnButtonUp;
    public event Action<ButtonToggledEventArgs>? OnToggled;

    public ContentButton()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        AddStyleClass(StyleClassButton);

        _label = new ContentLabel
        {
            HorizontalAlignment = HAlignment.Stretch,
            TextAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            WordWrap = false,
            Margin = new Thickness(8, 4)
        };
        AddChild(_label);
    }

    public ContentButton(string text) : this()
    {
        Text = text;
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _isBeingHovered = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _isBeingHovered = false;
        _isPressing = false;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (Disabled || args.Function != EngineKeyFunctions.UIClick)
            return;

        var btnArgs = new ButtonEventArgs(this, args);
        OnButtonDown?.Invoke(btnArgs);

        if (Mode == ActionMode.Press)
        {
            if (ToggleMode)
            {
                Pressed = !Pressed;
                OnToggled?.Invoke(new ButtonToggledEventArgs(Pressed, this, args));
            }
            UserInterfaceManager.ClickSound();
            OnPressed?.Invoke(btnArgs);
        }
        else
        {
            _isPressing = true;
        }

        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (Disabled || args.Function != EngineKeyFunctions.UIClick)
            return;

        var btnArgs = new ButtonEventArgs(this, args);
        OnButtonUp?.Invoke(btnArgs);

        if (_isPressing)
        {
            _isPressing = false;
            if (ToggleMode)
            {
                Pressed = !Pressed;
                OnToggled?.Invoke(new ButtonToggledEventArgs(Pressed, this, args));
            }
            UserInterfaceManager.ClickSound();
            OnPressed?.Invoke(btnArgs);
        }

        args.Handle();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize);

        var hasText = !string.IsNullOrEmpty(_label.Text);
        var labelSize = hasText ? _label.DesiredSize : Vector2.Zero;

        if (_icon == null)
            return new Vector2(MathF.Max(labelSize.X + 16, 24), MathF.Max(labelSize.Y + 8, 24));

        if (!hasText)
            return new Vector2(MathF.Max(_iconSize.X + 12, 26), MathF.Max(_iconSize.Y + 8, 24));

        return new Vector2(
            MathF.Max(6 + _iconSize.X + labelSize.X + 10, 28),
            MathF.Max(MathF.Max(_iconSize.Y + 8, labelSize.Y + 8), 24));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var hasText = !string.IsNullOrEmpty(_label.Text);
        var box = new UIBox2(Vector2.Zero, finalSize);
        var labelBox = _icon != null && hasText
            ? new UIBox2(MathF.Min(finalSize.X, 6 + _iconSize.X), 0f, finalSize.X, finalSize.Y)
            : box;

        foreach (var child in Children)
            child.Arrange(child == _label ? labelBox : box);

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        Color bgColor;
        Color borderColor;
        Color fgColor;

        if (Borderless)
        {
            if (Disabled)
            {
                bgColor = Color.Transparent;
                fgColor = Color.FromHex("#5A6270");
            }
            else if (_isPressing && _isBeingHovered)
            {
                bgColor = HoverColorOverride ?? Color.FromHex("#00D4FF38");
                fgColor = Color.White;
            }
            else if (_isBeingHovered)
            {
                bgColor = HoverColorOverride ?? Color.FromHex("#00D4FF22");
                fgColor = Color.White;
            }
            else if (_pressed)
            {
                bgColor = Color.Transparent;
                fgColor = Color.White;
            }
            else
            {
                bgColor = Color.Transparent;
                fgColor = Color.FromHex("#C8D6E5");
            }

            _label.FontColorOverride = fgColor;

            if (bgColor.A > 0f)
                handle.DrawRect(box, bgColor, filled: true);
        }
        else
        {
            if (Disabled)
            {
                bgColor = Color.FromHex("#1A1D24");
                borderColor = Color.FromHex("#2C3240");
                fgColor = Color.FromHex("#5A6270");
            }
            else if (_pressed || (_isPressing && _isBeingHovered))
            {
                bgColor = Color.FromHex("#00D4FF44");
                borderColor = Color.FromHex("#00D4FF");
                fgColor = Color.White;
            }
            else if (_isBeingHovered)
            {
                bgColor = Color.FromHex("#00D4FF22");
                borderColor = Color.FromHex("#00B8DE");
                fgColor = Color.FromHex("#E0F7FA");
            }
            else
            {
                bgColor = Color.FromHex("#151D28");
                borderColor = Color.FromHex("#2C3A4D");
                fgColor = Color.FromHex("#C8D6E5");
            }

            _label.FontColorOverride = fgColor;

            handle.DrawRect(box, bgColor, filled: true);
            handle.DrawRect(box, borderColor, filled: false);
        }

        if (_icon != null)
        {
            var iconW = MathF.Min(PixelWidth, _iconSize.X * UIScale);
            var iconH = MathF.Min(PixelHeight, _iconSize.Y * UIScale);
            if (iconW > 0f && iconH > 0f)
            {
                var hasText = !string.IsNullOrEmpty(_label.Text);
                var x = hasText ? 6f * UIScale : (PixelWidth - iconW) / 2f;
                var y = (PixelHeight - iconH) / 2f;
                var iconColor = Disabled ? fgColor : (_iconModulate ?? fgColor);
                handle.DrawTextureRect(_icon, UIBox2.FromDimensions(new Vector2(x, y), new Vector2(iconW, iconH)), iconColor);
            }
        }
    }
}
