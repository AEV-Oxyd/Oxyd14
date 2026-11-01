using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Computers.Cartridges;

public sealed partial class FileBrowserUi : UIFragment
{
    private FileBrowserUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new FileBrowserUiFragment();

        _fragment.OnChangeDirectory += path =>
            SendMessage(new FileBrowserUiMessageEvent(FileBrowserAction.ChangeDirectory, path: path), userInterface);

        _fragment.OnViewFile += path =>
            SendMessage(new FileBrowserUiMessageEvent(FileBrowserAction.ViewFile, path: path), userInterface);

        _fragment.OnClosePreview += () =>
            SendMessage(new FileBrowserUiMessageEvent(FileBrowserAction.CloseView), userInterface);

        _fragment.OnDeleteFile += path =>
            SendMessage(new FileBrowserUiMessageEvent(FileBrowserAction.DeleteFile, path: path), userInterface);

        _fragment.OnRunFile += path =>
            SendMessage(new FileBrowserUiMessageEvent(FileBrowserAction.RunProgram, path: path), userInterface);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not FileBrowserUiState fbState)
            return;

        _fragment?.UpdateState(fbState);
    }

    private void SendMessage(FileBrowserUiMessageEvent message, BoundUserInterface userInterface)
    {
        var cartridgeMsg = new CartridgeUiMessage(message);
        userInterface.SendPredictedMessage(cartridgeMsg);
    }
}
