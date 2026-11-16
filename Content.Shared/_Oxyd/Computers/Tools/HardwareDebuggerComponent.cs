using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Tools;

/// <summary>
/// Diagnostic terminal tool that connects locally to any computer equipped with a
/// <see cref="Hardware.ComputerDataPortComponent"/> for circuit, DeviceLink, hardware,
/// and filesystem debugging without requiring a network card.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HardwareDebuggerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? ConnectedComputer;

    [DataField]
    public string CurrentDirectory = "/";

    [DataField]
    public List<string> History = new()
    {
        "NT Hardware Debugging Terminal [DATA PORT]",
        "Type 'help' for hardware, DeviceLink, process, and filesystem commands."
    };
}

[Serializable, NetSerializable]
public sealed class HardwareDebuggerUiState : BoundUserInterfaceState
{
    public bool IsConnected { get; }
    public string TargetName { get; }
    public string TargetGuid { get; }
    public string DataPortLabel { get; }
    public string HostMachineName { get; }
    public bool IsBooted { get; }
    public string BootError { get; }
    public string HardwareSummary { get; }
    public List<MirroredDevicePortInfo> Ports { get; }
    public List<string> Lines { get; }
    public string Prompt { get; }

    public HardwareDebuggerUiState(
        bool isConnected,
        string targetName,
        string targetGuid,
        string dataPortLabel,
        string hostMachineName,
        bool isBooted,
        string bootError,
        string hardwareSummary,
        List<MirroredDevicePortInfo> ports,
        List<string> lines,
        string prompt)
    {
        IsConnected = isConnected;
        TargetName = targetName;
        TargetGuid = targetGuid;
        DataPortLabel = dataPortLabel;
        HostMachineName = hostMachineName;
        IsBooted = isBooted;
        BootError = bootError;
        HardwareSummary = hardwareSummary;
        Ports = ports;
        Lines = lines;
        Prompt = prompt;
    }
}

[Serializable, NetSerializable]
public sealed class HardwareDebuggerCommandMessage : BoundUserInterfaceMessage
{
    public string Command { get; }

    public HardwareDebuggerCommandMessage(string command)
    {
        Command = command;
    }
}

[Serializable, NetSerializable]
public sealed class HardwareDebuggerDisconnectMessage : BoundUserInterfaceMessage;
