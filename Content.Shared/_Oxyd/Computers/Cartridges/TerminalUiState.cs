using Content.Shared._Oxyd.Computers.ScreenSharing;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[Serializable, NetSerializable]
public sealed class TerminalUiState : BoundUserInterfaceState
{
    public List<string> Lines { get; }
    public string Prompt { get; }
    public string DeviceGuid { get; }
    public string? SshHost { get; }
    public bool IsPasswordPrompt { get; }
    public bool IsSshLocalMode { get; }
    public bool IsScreenAttached { get; }
    public Content.Shared.PDA.PdaUpdateState? RemotePdaState { get; }
    public RemoteScreenState? RemoteScreen { get; }

    public TerminalUiState(
        List<string> lines,
        string prompt,
        string deviceGuid,
        string? sshHost,
        bool isPasswordPrompt,
        bool isSshLocalMode = false,
        bool isScreenAttached = false,
        Content.Shared.PDA.PdaUpdateState? remotePdaState = null,
        RemoteScreenState? remoteScreen = null)
    {
        Lines = lines;
        Prompt = prompt;
        DeviceGuid = deviceGuid;
        SshHost = sshHost;
        IsPasswordPrompt = isPasswordPrompt;
        IsSshLocalMode = isSshLocalMode;
        IsScreenAttached = isScreenAttached;
        RemotePdaState = remotePdaState;
        RemoteScreen = remoteScreen;
    }
}
