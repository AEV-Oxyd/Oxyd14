namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Marks a physical door as eligible for the foundation door litanies.
/// Access remains supplied dynamically by the current bearer.
/// </summary>
[RegisterComponent]
public sealed partial class NeoTheologyDoorComponent : Component
{
    [DataField]
    public bool LitanyLocked;
}
