using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Relationship component installed on the current body of a real cruciform.
/// The server verifies that <see cref="Cruciform"/> is an actual contained implant
/// before accepting any authority-bearing operation.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformBearerComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Cruciform;

    [ViewVariables, AutoNetworkedField]
    public uint UiRevision;

    /// <summary>Server-only personal cooldown deadlines, keyed by stable litany key.</summary>
    [ViewVariables]
    public Dictionary<string, TimeSpan> PersonalCooldowns = new();

    /// <summary>Server-only request ID for the one pending cast/choice.</summary>
    [ViewVariables]
    public string? PendingRequestId;
}
