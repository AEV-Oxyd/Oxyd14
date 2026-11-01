using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[Serializable, NetSerializable]
public sealed class TerminalUiMessageEvent : CartridgeMessageEvent
{
    public string CommandText { get; }

    public TerminalUiMessageEvent(string commandText)
    {
        CommandText = commandText;
    }
}
