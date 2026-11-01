using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Network;

/// <summary>
/// Provides a short 8-character hexadecimal GUID for identifying physical devices and electronics on the network.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DeviceGuidComponent : Component
{
    /// <summary>
    /// Short 8-character hex GUID (e.g. "E4A28B10").
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Guid = string.Empty;
}
