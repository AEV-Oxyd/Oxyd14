using System.Linq;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A line edit control derived from <see cref="ContentControl"/> that properly handles keyboard focus,
/// typing, editing, and input relaying from remote screen streams.
/// </summary>
public class ContentLineEdit : ContentControl
{
    public class LineEditEventArgs : EventArgs
    {
        public ContentLineEdit LineEdit { get; }
        public string Text { get; }

        public LineEditEventArgs(ContentLineEdit lineEdit, string text)
        {
            LineEdit = lineEdit;
            Text = text;
        }
    }

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set
        {
            var old = _text;
            _text = value ?? string.Empty;
            _cursorPos = Math.Clamp(_cursorPos, 0, _text.Length);
            if (old != _text)
            {
                OnTextChanged?.Invoke(new LineEditEventArgs(this, _text));
                InvalidateMeasure();
            }
        }
    }

    public string? PlaceHolder { get; set; }
    public bool Editable { get; set; } = true;

    private int _cursorPos;
    private bool _caretBlink;
    private float _caretTimer;

    public event Action<LineEditEventArgs>? OnTextEntered;
    public event Action<LineEditEventArgs>? OnTextChanged;

    public ContentLineEdit()
    {
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MinHeight = 24;
    }

    public void Clear()
    {
        Text = string.Empty;
        _cursorPos = 0;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            GrabKeyboardFocus();
            _cursorPos = _text.Length;
            args.Handle();
            return;
        }

        if (!HasKeyboardFocus() || !Editable)
            return;

        if (args.Function == EngineKeyFunctions.TextSubmit || args.Function == EngineKeyFunctions.TextNewline)
        {
            OnTextEntered?.Invoke(new LineEditEventArgs(this, _text));
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.TextBackspace)
        {
            if (_cursorPos > 0 && _text.Length > 0)
            {
                Text = _text.Remove(_cursorPos - 1, 1);
                _cursorPos--;
            }
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.TextDelete)
        {
            if (_cursorPos < _text.Length)
            {
                Text = _text.Remove(_cursorPos, 1);
            }
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.TextCursorLeft)
        {
            if (_cursorPos > 0)
                _cursorPos--;
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.TextCursorRight)
        {
            if (_cursorPos < _text.Length)
                _cursorPos++;
            args.Handle();
            return;
        }
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        base.TextEntered(args);

        if (!Editable || string.IsNullOrEmpty(args.Text))
            return;

        var filtered = new string(args.Text.Where(c => !char.IsControl(c)).ToArray());
        if (string.IsNullOrEmpty(filtered))
            return;

        Text = _text.Insert(_cursorPos, filtered);
        _cursorPos += filtered.Length;
    }

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (HasKeyboardFocus())
        {
            _caretTimer += args.DeltaSeconds;
            if (_caretTimer >= 0.5f)
            {
                _caretTimer -= 0.5f;
                _caretBlink = !_caretBlink;
            }
        }
        else
        {
            _caretBlink = false;
        }
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
        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        var textToMeasure = string.IsNullOrEmpty(_text) ? (PlaceHolder ?? "A") : _text;
        var dim = MeasureString(font, textToMeasure);
        return new Vector2(MathF.Max((dim.X + 16) / UIScale, 60), MathF.Max((dim.Y + 8) / UIScale, MinHeight));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        var hasFocus = HasKeyboardFocus();

        var bgColor = Color.FromHex("#0A0E17");
        var borderColor = hasFocus ? Color.FromHex("#00D4FF") : Color.FromHex("#2C3A4D");

        handle.DrawRect(box, bgColor, filled: true);
        handle.DrawRect(box, borderColor, filled: false);

        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        var padding = 6f;
        var textY = Math.Max(0f, (PixelHeight - font.GetHeight(UIScale)) / 2f);

        if (string.IsNullOrEmpty(_text))
        {
            if (!string.IsNullOrEmpty(PlaceHolder))
            {
                handle.DrawString(font, new Vector2(padding, textY), PlaceHolder, UIScale, Color.FromHex("#5A6A7A"));
            }
        }
        else
        {
            handle.DrawString(font, new Vector2(padding, textY), _text, UIScale, Color.FromHex("#E0F7FA"));
        }

        if (hasFocus && _caretBlink)
        {
            var textBeforeCursor = _text.Substring(0, Math.Clamp(_cursorPos, 0, _text.Length));
            var cursorOffset = MeasureString(font, textBeforeCursor).X;
            var caretX = padding + cursorOffset;
            handle.DrawRect(new UIBox2(caretX, 4, caretX + 1.5f, PixelHeight - 4), Color.FromHex("#00D4FF"));
        }
    }
}
