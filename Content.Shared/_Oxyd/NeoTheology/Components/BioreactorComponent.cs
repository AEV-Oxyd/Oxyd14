using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// A flattened Eris bioreactor: the platform/pump multistructure is one machine and its three
/// chamber booleans drive everything.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BioreactorComponent : Component
{
    [DataField, AutoNetworkedField] public bool ChamberClosed = true;

    [DataField, AutoNetworkedField] public bool ChamberSolution;

    [DataField, AutoNetworkedField] public bool ChamberBreached;

    [DataField] public float ProcessingRadius = 3f;

    [DataField] public int BiomatterPerEntity = 10;
}
