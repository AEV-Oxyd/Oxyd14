using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FileBrowserCartridgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public string CurrentDirectory = "/";

    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;
}
