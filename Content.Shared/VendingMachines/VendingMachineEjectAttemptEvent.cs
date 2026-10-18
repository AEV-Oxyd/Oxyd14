namespace Content.Shared.VendingMachines;

/// <summary>
/// Raised on a vending machine before an authorized eject from the UI.
/// Cancel to block the vend (e.g. unpaid Oxyd economy vendors).
/// </summary>
[ByRefEvent]
public record struct VendingMachineEjectAttemptEvent(
    EntityUid User,
    InventoryType Type,
    string ItemId,
    bool Cancelled = false,
    string? Reason = null);
