using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// A flattened Eris biogenerator: the console/port/generator/chamber part graph is one machine.
/// It burns biomatter from its own <c>MaterialStorage</c> for power.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BiogeneratorComponent : Component
{
    [DataField] public float OutputWatts = 500_000f;

    /// <summary>Biomatter burned per second while working.</summary>
    [DataField] public float BiomatterPerSecond = 1f;

    /// <summary>0..1, degrades output as the machine wears out.</summary>
    [DataField] public float Dirtiness;

    /// <summary>Fractional biomatter owed but not yet a whole unit, carried between ticks.</summary>
    [ViewVariables] public float BiomatterAccumulator;

    [DataField, AutoNetworkedField] public bool Working;
}
