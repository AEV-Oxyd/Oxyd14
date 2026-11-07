using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class NotekeeperUiMessageEvent : CartridgeMessageEvent
{
    public readonly NotekeeperUiAction Action;
    public readonly string Path;
    public readonly string Content;

    public NotekeeperUiMessageEvent(NotekeeperUiAction action, string path = "", string content = "")
    {
        Action = action;
        Path = path;
        Content = content;
    }
}

[Serializable, NetSerializable]
public enum NotekeeperUiAction
{
    Add,
    Remove,
    OpenFile,
    SaveFile,
    CreateFile,
    DeleteFile
}
