using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Computers.Cartridges;

public sealed partial class DownloaderUi : UIFragment
{
    private DownloaderUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new DownloaderUiFragment();

        _fragment.OnRefresh += () =>
            SendMessage(new DownloaderUiMessageEvent(DownloaderAction.Refresh), userInterface);

        _fragment.OnSelectCategory += cat =>
            SendMessage(new DownloaderUiMessageEvent(DownloaderAction.SelectCategory, parameter: cat), userInterface);

        _fragment.OnDownloadItem += itemId =>
            SendMessage(new DownloaderUiMessageEvent(DownloaderAction.DownloadItem, parameter: itemId), userInterface);

        _fragment.OnInstallProgram += itemId =>
            SendMessage(new DownloaderUiMessageEvent(DownloaderAction.InstallProgram, parameter: itemId), userInterface);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not DownloaderUiState dlState)
            return;

        _fragment?.UpdateState(dlState);
    }

    private void SendMessage(DownloaderUiMessageEvent message, BoundUserInterface userInterface)
    {
        var cartridgeMsg = new CartridgeUiMessage(message);
        userInterface.SendPredictedMessage(cartridgeMsg);
    }
}
