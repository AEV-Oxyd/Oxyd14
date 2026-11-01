using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TerminalCartridgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<string> History = new()
    {
        "NT-OS SSH Terminal v4.2 [Oxyd System Subsystem]",
        "Type 'help' for available commands or 'ssh <guid>' to connect to remote host.",
        ""
    };

    [DataField, AutoNetworkedField]
    public string? ConnectedRemoteGuid;

    [DataField, AutoNetworkedField]
    public string? ConnectedRemoteUser;

    [DataField, AutoNetworkedField]
    public bool AwaitingPassword;

    [DataField, AutoNetworkedField]
    public string PendingSshGuid = string.Empty;

    [DataField, AutoNetworkedField]
    public string PendingSshUser = "admin";
}
