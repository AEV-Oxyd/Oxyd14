using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers;

/// <summary>
/// Primary display BUI key on standalone, embedded, and workstation computer entities.
/// PDAs additionally map <see cref="Content.Shared.PDA.PdaUiKey.Key"/> to the same display BUI.
/// </summary>
[Serializable, NetSerializable]
public enum ComputerUiKey : byte
{
    Key
}

/// <summary>
/// Universal BUI key attached to every running Program entity.
/// Each program entity delivers its own bound UI states directly to connected clients.
/// </summary>
[Serializable, NetSerializable]
public enum ProgramUiKey : byte
{
    Key
}

/// <summary>
/// Local data-port debugger terminal BUI key on <c>HardwareDebuggingTerminal</c> tools.
/// </summary>
[Serializable, NetSerializable]
public enum HardwareDebuggerUiKey : byte
{
    Key
}
