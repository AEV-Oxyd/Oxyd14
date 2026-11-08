using Content.Shared.Power.Generator;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// A flattened Eris biogenerator: the console/port/generator/chamber part graph is one machine.
/// Burning biomatter for power rides the stock <see cref="FuelGeneratorComponent"/> +
/// <c>SolidFuelGeneratorAdapterComponent</c> path; this component only marks the machine for the
/// NeoTheology litanies and carries its Eris fouling state.
/// </summary>
[RegisterComponent]
public sealed partial class BiogeneratorComponent : Component
{
    /// <summary>0..1, degrades output as the machine wears out.</summary>
    [DataField]
    public float Dirtiness;
}
