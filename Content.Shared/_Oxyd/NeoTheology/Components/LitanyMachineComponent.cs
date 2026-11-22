using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Marker for entities litanies treat as NeoTheology machines (FrontMachine/NearbyMachine
/// target shapes). Attached on the machine entity prototypes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LitanyMachineComponent : Component;
