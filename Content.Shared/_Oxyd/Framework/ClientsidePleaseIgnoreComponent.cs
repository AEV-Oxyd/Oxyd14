using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Framework;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClientsidePleaseIgnoreComponent : Component
{
    [DataField,AutoNetworkedField]
    public HashSet<string> forSessions = new HashSet<string>();
}
