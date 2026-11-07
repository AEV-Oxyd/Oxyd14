using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface;

namespace Content.Client.CartridgeLoader.Cartridges;

public sealed partial class NotekeeperUi : UIFragment
{
    private NotekeeperUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new NotekeeperUiFragment();
        _fragment.OnNoteRemoved += note => SendNotekeeperMessage(NotekeeperUiAction.Remove, note, "", userInterface);
        _fragment.OnNoteAdded += note => SendNotekeeperMessage(NotekeeperUiAction.Add, note, "", userInterface);
        _fragment.OnOpenFile += path => SendNotekeeperMessage(NotekeeperUiAction.OpenFile, path, "", userInterface);
        _fragment.OnSaveFile += (path, content) => SendNotekeeperMessage(NotekeeperUiAction.SaveFile, path, content, userInterface);
        _fragment.OnCreateFile += (name, content) => SendNotekeeperMessage(NotekeeperUiAction.CreateFile, name, content, userInterface);
        _fragment.OnDeleteFile += path => SendNotekeeperMessage(NotekeeperUiAction.DeleteFile, path, "", userInterface);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not NotekeeperUiState notekeeperState)
            return;

        _fragment?.UpdateState(notekeeperState);
    }

    private void SendNotekeeperMessage(NotekeeperUiAction action, string path, string content, BoundUserInterface userInterface)
    {
        var notekeeperMessage = new NotekeeperUiMessageEvent(action, path, content);
        var message = new CartridgeUiMessage(notekeeperMessage);
        userInterface.SendPredictedMessage(message);
    }
}
