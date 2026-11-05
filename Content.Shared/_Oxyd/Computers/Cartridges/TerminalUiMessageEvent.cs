using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[Serializable, NetSerializable]
public sealed class TerminalUiMessageEvent : CartridgeMessageEvent
{
    public string CommandText { get; }
    public string? RemoteAction { get; }
    public string? RemoteActionArg { get; }
    public NetEntity? TargetProgram { get; }

    public TerminalUiMessageEvent(
        string commandText,
        string? remoteAction = null,
        string? remoteActionArg = null,
        NetEntity? targetProgram = null)
    {
        CommandText = commandText;
        RemoteAction = remoteAction;
        RemoteActionArg = remoteActionArg;
        TargetProgram = targetProgram;
    }
}
