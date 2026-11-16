using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Universal base control for all computer programs.
/// Implements IInputRelayTarget via ContentControl so that inputs can be forwarded
/// across off-screen trees and remote screen sessions without reflection or sandboxing issues.
/// </summary>
public abstract class ProgramUiControl : ContentControl
{
    private Action<BoundUserInterfaceMessage>? _sendMessage;

    public EntityUid? ProgramUid { get; set; }
    public EntityUid? ComputerUid { get; set; }

    public void BindSendMessage(Action<BoundUserInterfaceMessage> sendMessage)
    {
        _sendMessage = sendMessage;
    }

    protected void SendProgramMessage(BoundUserInterfaceMessage msg)
    {
        _sendMessage?.Invoke(msg);
    }

    public virtual void UpdateProgramState(BoundUserInterfaceState state)
    {
    }

    public virtual void OnAppActivated()
    {
    }

    public virtual void OnAppDeactivated()
    {
    }
}
