using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

public enum DownloaderAction : byte
{
    Refresh,
    SelectCategory,
    DownloadItem,
    InstallProgram
}

[Serializable, NetSerializable]
public sealed class DownloaderUiMessageEvent : CartridgeMessageEvent
{
    public DownloaderAction Action { get; }
    public string Parameter { get; }

    public DownloaderUiMessageEvent(DownloaderAction action, string parameter = "")
    {
        Action = action;
        Parameter = parameter;
    }
}
