namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>
/// Bridge for Revelation: litany effects are prototype data in shared code and cannot
/// reach the server-only <c>SanitySystem</c>. The effect raises this on the target body;
/// the server handler applies the delta and sets <see cref="Handled"/>. A false
/// <see cref="Handled"/> is the effect's failure path (no subscriber, nothing applied).
/// </summary>
[ByRefEvent]
public record struct LitanySanityDeltaEvent(EntityUid Target, float Amount, bool Handled);

/// <summary>
/// Bridge for Epiphany: raised on the target body; the server <c>CruciformSystem</c>
/// activates the installed cruciform and sets <see cref="Handled"/>. A false
/// <see cref="Handled"/> means the target has no installed, inactive cruciform.
/// </summary>
[ByRefEvent]
public record struct LitanyActivateCruciformEvent(EntityUid Target, bool Handled);
