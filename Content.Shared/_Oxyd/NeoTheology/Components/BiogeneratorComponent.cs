using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// A flattened Eris biogenerator: the console/port/generator/chamber part graph is one machine.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BiogeneratorComponent : Component
{
    [DataField] public float OutputWatts = 500_000f;

    [DataField] public float BiomatterPerSecond = 1f;

    /// <summary>0..1, degrades output as the machine wears out.</summary>
    [DataField] public float Dirtiness;

    [DataField, AutoNetworkedField] public bool Working;
}
