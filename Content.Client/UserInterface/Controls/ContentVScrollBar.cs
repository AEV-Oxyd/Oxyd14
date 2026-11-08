using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// A VScrollBar that derives from the engine <see cref="VScrollBar"/> and implements
/// <see cref="IInputRelayTarget"/> so that remote screens and wrappers can relay inputs directly.
/// </summary>
public class ContentVScrollBar : VScrollBar, IInputRelayTarget
{
    public virtual void RelayKeyBindDown(GUIBoundKeyEventArgs args)
    {
        KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            args.Handle();
        }
    }

    public virtual void RelayKeyBindUp(GUIBoundKeyEventArgs args)
    {
        KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            args.Handle();
        }
    }

    public virtual void RelayMouseMove(GUIMouseMoveEventArgs args)
    {
        MouseMove(args);
    }

    public virtual void RelayMouseWheel(GUIMouseWheelEventArgs args)
    {
        MouseWheel(args);
    }

    public virtual void RelayMouseEntered()
    {
        MouseEntered();
    }

    public virtual void RelayMouseExited()
    {
        MouseExited();
    }

    public virtual void RelayKeyHeld(GUIKeyEventArgs args)
    {
        KeyHeld(args);
    }

    public virtual void RelayTextEntered(GUITextEnteredEventArgs args)
    {
        TextEntered(args);
    }

    public virtual void RelayTextEditing(GUITextEditingEventArgs args)
    {
        TextEditing(args);
    }

    public virtual void RelayKeyboardFocusEntered()
    {
        KeyboardFocusEntered();
    }

    public virtual void RelayKeyboardFocusExited()
    {
        KeyboardFocusExited();
    }

    public virtual void RelayControlFocusExited()
    {
        ControlFocusExited();
    }
}
