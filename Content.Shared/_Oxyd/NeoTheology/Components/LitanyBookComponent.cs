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

    /// <summary>
    /// Server-side set of actors currently viewing this book's UI. Used to send private
    /// viewer snapshots and to revoke pending book casts on close/drop. Never networked.
    /// </summary>
    public readonly HashSet<EntityUid> Viewers = new();

    /// <summary>
    /// Last sent snapshot signature per viewer — snapshots push only when it changes.
    /// </summary>
    public readonly Dictionary<EntityUid, LitanyViewerSignature> LastSent = new();
}

/// <summary>
/// Cheap digest of what the viewer snapshot would contain. The book re-sends a
/// snapshot only when any of these values change — the client extrapolates holiness
/// and cooldown countdowns between pushes.
/// </summary>
public readonly record struct LitanyViewerSignature(
    uint Revision,
    int HolinessBucket,
    int CooldownCount,
    int CooldownNextEnd,
    int BusyStage,
    string? BusyRequestId);
