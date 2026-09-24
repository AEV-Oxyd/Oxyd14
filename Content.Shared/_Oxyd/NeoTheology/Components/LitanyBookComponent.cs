namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Presentation marker for the physical NeoTheology book. It stores no authority,
/// holiness, role, target, or per-player selection state.
/// </summary>
[RegisterComponent]
public sealed partial class LitanyBookComponent : Component
{
    [DataField]
    public bool ReferenceCatalog = true;
}
