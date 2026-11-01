using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[Serializable, NetSerializable]
public enum FileBrowserAction : byte
{
    ChangeDirectory,
    ViewFile,
    CloseView,
    DeleteFile,
    RunProgram,
    DownloadFile,
    PingDevice,
    RemoteControl
}

[Serializable, NetSerializable]
public sealed class FileBrowserUiMessageEvent : CartridgeMessageEvent
{
    public FileBrowserAction Action { get; }
    public string Path { get; }
    public string TargetGuid { get; }
    public string Command { get; }
    public string AuthToken { get; }

    public FileBrowserUiMessageEvent(
        FileBrowserAction action,
        string path = "",
        string targetGuid = "",
        string command = "",
        string authToken = "")
    {
        Action = action;
        Path = path;
        TargetGuid = targetGuid;
        Command = command;
        AuthToken = authToken;
    }
}
