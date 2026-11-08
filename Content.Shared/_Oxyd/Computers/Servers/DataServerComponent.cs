using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Servers;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DataServerComponent : Component
{
    /// <summary>
    /// Configured protocols/services this server responds to.
    /// Declared in YML prototypes (e.g. "ManifestService", "StationRecordsService", "ResearchService").
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> ServerProtocols = new();

    /// <summary>
    /// Authentication master key for this server.
    /// Can be altered mid-round by authorized station heads or engineers.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string NetworkMasterKey = "NanotrasenDefaultRoot";

    [DataField, AutoNetworkedField]
    public bool AcceptsPublicReads = true;
}
