using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.12: a NeoTheology reader. Its item slot is where the litany hands a cruciform over; whatever
/// sits in that slot is the implant whose soul (see <see cref="CruciformSoulComponent"/>) gets read
/// back out.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformReaderComponent : Component
{
    /// <summary>
    /// The item slot the cruciform goes into.
    /// </summary>
    [DataField]
    public string SlotId = "cruciform";

    /// <summary>
    /// The cruciform currently in the slot, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ReaderImplant;
}
