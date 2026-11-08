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
    private bool _disabled;
    private bool _pressed;
    private bool _isBeingHovered;
    private bool _isPressing;

    public ContentLabel Label => _label;

    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
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
        AddStyleClass(StyleClassButton);

        _label = new ContentLabel
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
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
        _label.Measure(availableSize);
        var labelSize = _label.DesiredSize;
        return new Vector2(MathF.Max(labelSize.X + 16, 24), MathF.Max(labelSize.Y + 8, 24));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _label.Arrange(new UIBox2(Vector2.Zero, finalSize));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        Color bgColor;
        Color borderColor;

        if (Disabled)
        {
            bgColor = Color.FromHex("#1A1D24");
            borderColor = Color.FromHex("#2C3240");
            _label.FontColorOverride = Color.FromHex("#5A6270");
        }
        else if (_pressed || (_isPressing && _isBeingHovered))
        {
            bgColor = Color.FromHex("#00D4FF44");
            borderColor = Color.FromHex("#00D4FF");
            _label.FontColorOverride = Color.White;
        }
        else if (_isBeingHovered)
        {
            bgColor = Color.FromHex("#00D4FF22");
            borderColor = Color.FromHex("#00B8DE");
            _label.FontColorOverride = Color.FromHex("#E0F7FA");
        }
        else
        {
            bgColor = Color.FromHex("#151D28");
            borderColor = Color.FromHex("#2C3A4D");
            _label.FontColorOverride = Color.FromHex("#C8D6E5");
        }

        handle.DrawRect(box, bgColor, filled: true);
        handle.DrawRect(box, borderColor, filled: false);
    }
}
