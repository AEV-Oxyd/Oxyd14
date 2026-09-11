using Robust.Shared.Prototypes;

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

/// <summary>
/// Bridge for Adoption: raised on the target, who carries no cruciform yet and therefore has no
/// component to subscribe on — <c>LitanyEffectSystem.RaiseOn</c> broadcasts so server systems can
/// still reach them. The server <c>CruciformSystem</c> spawns, implants and activates a fresh
/// cruciform with <see cref="Profile"/> and sets <see cref="Handled"/>.
/// </summary>
[ByRefEvent]
public record struct LitanyGrantCruciformEvent(
    EntityUid Target,
    ProtoId<NeoTheologyProfilePrototype> Profile,
    bool Handled);

/// <summary>
/// Bridge for the role-change litanies (Confirmation, Ordination, Omission, Excommunication):
/// raised on the target body; the server <c>CruciformSystem</c> swaps the installed cruciform's
/// profile and rank modules in one operation and sets <see cref="Handled"/>. A false
/// <see cref="Handled"/> means the target has no installed cruciform.
/// </summary>
[ByRefEvent]
public record struct LitanySetRankEvent(
    EntityUid Target,
    ProtoId<NeoTheologyProfilePrototype> Profile,
    bool Handled);
