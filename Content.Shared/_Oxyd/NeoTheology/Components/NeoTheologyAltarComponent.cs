using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class NeoTheologyAltarComponent : Component
{
    /// <summary>Radius searched for ritual items placed on the altar.</summary>
    [DataField]
    public float Radius = 1f;
}
