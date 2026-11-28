using Content.Shared._Oxyd.Computers.ScreenSharing;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent]
public sealed partial class TerminalProgramComponent : Component
{
    [DataField]
    public string CurrentDirectory = "/";

    [DataField]
    public List<string> History = new()
    {
        "NTOS Diagnostic Shell v1.4",
        "Type 'help' for available commands."
    };

    [DataField]
    public string? ConnectedRemoteGuid;

    [DataField]
    public string? ConnectedRemoteUser;

    /// <summary>
    /// Snapshot of the target computer's LockEpoch at the moment this SSH session authenticated.
    /// If the target subsequently re-locks or reboots with a new epoch, this session is invalidated.
    /// </summary>
    [DataField]
    public int ConnectedRemoteLockEpoch;

    [DataField]
    public string? PendingAuthGuid;

    [DataField]
    public string? PendingAuthUser;

    [DataField]
    public bool AwaitingPassword;

    [DataField]
    public bool SshLocalMode;

    [DataField]
    public bool IsScreenAttached;

    [DataField]
    public string RemoteDirectory = "/";

    /// <summary>
    /// True when connected over a direct physical JTAG diagnostic cable (e.g. OxydDebugController)
    /// instead of a routed network SSH session.
    /// </summary>
    [DataField]
    public bool IsPhysicalLink;

    [DataField]
    public EntityUid? PhysicalTargetComputer;

    [DataField]
    public float PhysicalMaxRange = 3.5f;

    [ViewVariables]
    public HashSet<EntityUid> ActiveViewers = new();
}

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
    public bool HasNetworkCard { get; }
    public bool IsRouterOnline { get; }
    public RemoteScreenState? RemoteScreen { get; }
    public string? NetworkLabel { get; }
    public bool IsPhysicalLink { get; }

    public TerminalUiState(
        List<string> lines,
        string prompt,
        string deviceGuid,
        string? sshHost,
        bool isPasswordPrompt,
        bool isSshLocalMode,
        bool isScreenAttached,
        bool hasNetworkCard,
        bool isRouterOnline,
        RemoteScreenState? remoteScreen = null,
        string? networkLabel = null,
        bool isPhysicalLink = false)
    {
        Lines = lines;
        Prompt = prompt;
        DeviceGuid = deviceGuid;
        SshHost = sshHost;
        IsPasswordPrompt = isPasswordPrompt;
        IsSshLocalMode = isSshLocalMode;
        IsScreenAttached = isScreenAttached;
        HasNetworkCard = hasNetworkCard;
        IsRouterOnline = isRouterOnline;
        RemoteScreen = remoteScreen;
        NetworkLabel = networkLabel;
        IsPhysicalLink = isPhysicalLink;
    }
}

[Serializable, NetSerializable]
public sealed class TerminalExecuteCommandMessage : BoundUserInterfaceMessage
{
    public string CommandText { get; }

    public TerminalExecuteCommandMessage(string commandText)
    {
        CommandText = commandText;
    }
}

[Serializable, NetSerializable]
public sealed class TerminalRemoteScreenActionMessage : BoundUserInterfaceMessage
{
    public string Action { get; }
    public string? Argument { get; }

    public TerminalRemoteScreenActionMessage(string action, string? argument = null)
    {
        Action = action;
        Argument = argument;
    }
}
