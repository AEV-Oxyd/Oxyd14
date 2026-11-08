using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

/// <summary>
/// Interface for UI controls that can receive relayed input events from remote screens or wrappers,
/// bypassing engine assembly-internal protected method access restrictions.
/// </summary>
public interface IInputRelayTarget
{
    void RelayKeyBindDown(GUIBoundKeyEventArgs args);
    void RelayKeyBindUp(GUIBoundKeyEventArgs args);
    void RelayMouseMove(GUIMouseMoveEventArgs args);
    void RelayMouseWheel(GUIMouseWheelEventArgs args);
    void RelayMouseEntered();
    void RelayMouseExited();
    void RelayKeyHeld(GUIKeyEventArgs args);
    void RelayTextEntered(GUITextEnteredEventArgs args);
    void RelayTextEditing(GUITextEditingEventArgs args);
    void RelayKeyboardFocusEntered();
    void RelayKeyboardFocusExited();
    void RelayControlFocusExited();
}

/// <summary>
/// Base Control class for Content that derives from the engine Control class and implements
/// input relaying. All custom controls in Content should use this (or a class derived from it)
/// as their base parent class so that inputs can be properly forwarded across remote streams.
/// </summary>
public class ContentControl : Control, IInputRelayTarget
{
    public virtual void RelayKeyBindDown(GUIBoundKeyEventArgs args)
    {
        KeyBindDown(args);
    }

    public virtual void RelayKeyBindUp(GUIBoundKeyEventArgs args)
    {
        KeyBindUp(args);
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

    public static bool TryRelayKeyBindDown(Control control, GUIBoundKeyEventArgs args)
    {
        if (control is IInputRelayTarget target)
        {
            target.RelayKeyBindDown(args);
            return true;
        }
        return false;
    }

    public static bool TryRelayKeyBindUp(Control control, GUIBoundKeyEventArgs args)
    {
        if (control is IInputRelayTarget target)
        {
            target.RelayKeyBindUp(args);
            return true;
        }
        return false;
    }

    public static bool TryRelayMouseMove(Control control, GUIMouseMoveEventArgs args)
    {
        if (control is IInputRelayTarget target)
        {
            target.RelayMouseMove(args);
            return true;
        }
        return false;
    }

    public static bool TryRelayMouseWheel(Control control, GUIMouseWheelEventArgs args)
    {
        if (control is IInputRelayTarget target)
        {
            target.RelayMouseWheel(args);
            return true;
        }
        return false;
    }

    public static bool TryRelayTextEntered(Control control, GUITextEnteredEventArgs args)
    {
        if (control is IInputRelayTarget target)
        {
            target.RelayTextEntered(args);
            return true;
        }
        return false;
    }
}
