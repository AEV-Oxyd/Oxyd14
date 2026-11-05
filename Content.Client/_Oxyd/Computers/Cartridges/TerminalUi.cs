using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Computers.Cartridges;

public sealed partial class TerminalUi : UIFragment
{
    private TerminalUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new TerminalUiFragment { HostBui = userInterface };
        var targetProg = fragmentOwner.HasValue
            ? IoCManager.Resolve<IEntityManager>().GetNetEntity(fragmentOwner.Value)
            : (NetEntity?)null;

        _fragment.OnSendCommand += cmd =>
            SendMessage(new TerminalUiMessageEvent(cmd, targetProgram: targetProg), userInterface);

        _fragment.OnRemoteAction += (act, arg) =>
            SendMessage(new TerminalUiMessageEvent(string.Empty, act, arg, targetProgram: targetProg), userInterface);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not TerminalUiState termState)
            return;

        _fragment?.UpdateState(termState);
    }

    private void SendMessage(TerminalUiMessageEvent message, BoundUserInterface userInterface)
    {
        var cartridgeMsg = new CartridgeUiMessage(message);
        userInterface.SendPredictedMessage(cartridgeMsg);
    }
}
