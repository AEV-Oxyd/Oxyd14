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

/// <summary>
/// Bridge for InstallUpgrade: raised on the target follower; the server
/// <c>CruciformUpgradeSystem</c> finds the loose upgrade resting on the altar beside them,
/// attaches it to their cruciform and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means there was no altar, no item, or the slot was already taken.
/// </summary>
[ByRefEvent]
public record struct LitanyInstallUpgradeEvent(EntityUid Target, bool Handled);

/// <summary>
/// Bridge for UninstallUpgrade: raised on the target follower; the server
/// <c>CruciformUpgradeSystem</c> detaches the installed upgrade, returns the item to the
/// bearer's turf (the altar) and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means the target has no attached upgrade.
/// </summary>
[ByRefEvent]
public record struct LitanyUninstallUpgradeEvent(EntityUid Target, bool Handled);

/// <summary>
/// Bridge for Reincarnation: raised on the living target body; the server
/// <c>CoreModuleBehaviorSystem</c> writes a fresh soul snapshot from the wearer onto their
/// installed cruciform and sets <see cref="Handled"/>. A false <see cref="Handled"/> means
/// the snapshot write could not run (no installed cruciform to write onto).
/// </summary>
[ByRefEvent]
public record struct LitanyWriteSoulSnapshotEvent(EntityUid Target, bool Handled);

/// <summary>
/// Bridge for Resurrection: raised on the NeoTheology cloner among the litany's machine targets;
/// the server <c>CruciformReaderSystem</c> reads the soul out of <see cref="Reader"/>, starts
/// upstream <c>CloningPodSystem</c>'s own job for the dead wearer and sets
/// <see cref="Handled"/>. A false <see cref="Handled"/> means the soul, the corpse, the client
/// session or the pod's biomatter was not available.
/// </summary>
[ByRefEvent]
public record struct LitanyResurrectionEvent(EntityUid Cloner, EntityUid Reader, bool Handled);

/// <summary>
/// Bridge for MakeCruciform: raised on the NeoTheology forge among the litany's machine targets;
/// the server <c>CruciformForgeSystem</c> starts the forge's own produce run and sets
/// <see cref="Handled"/>. A false <see cref="Handled"/> means the forge was already working or
/// short of its recipe.
/// </summary>
[ByRefEvent]
public record struct LitanyForgeProduceEvent(EntityUid Forge, bool Handled);

/// <summary>
/// Bridge for RepairDoor: raised on the holy door; the server <c>NeoTheologyDoorSystem</c>
/// heals it and burns <paramref name="User"/>'s biomatter, then sets <see cref="Handled"/>.
/// A false <see cref="Handled"/> means the door was undamaged or no biomatter was in reach.
/// </summary>
[ByRefEvent]
public record struct LitanyRepairDoorEvent(EntityUid Door, EntityUid User, bool Handled);

/// <summary>
/// Bridge for PowerBiogenerator: raised on the NeoTheology biogenerator; the server
/// <c>BiogeneratorSystem</c> flips its working state and sets <see cref="Handled"/>. Eris
/// toggles the multistructure either way, so <see cref="Handled"/> is always true once reached.
/// </summary>
[ByRefEvent]
public record struct LitanyToggleBiogeneratorEvent(EntityUid Biogenerator, bool Handled);

/// <summary>
/// Bridge for BioreactorSolution: raised on the bioreactor; the server <c>BioreactorSystem</c>
/// pumps its chamber in or out and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means the chamber was open or breached.
/// </summary>
[ByRefEvent]
public record struct LitanyPumpBioreactorEvent(EntityUid Bioreactor, bool Handled);

/// <summary>
/// Bridge for BioreactorChamber: raised on the bioreactor; the server <c>BioreactorSystem</c>
/// opens or shuts the chamber door and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means the door was still jammed or the chamber still held solution.
/// </summary>
[ByRefEvent]
public record struct LitanyToggleBioreactorChamberEvent(EntityUid Bioreactor, bool Handled);

/// <summary>
/// Bridge for Scrying: raised on the scried body; the server <c>ScryingSystem</c> binds
/// <see cref="Caster"/>'s eye to <see cref="Target"/>'s surroundings for <see cref="Duration"/>
/// and sets <see cref="Handled"/>. A false <see cref="Handled"/> means the caster had no eye or
/// already had a live session.
/// </summary>
[ByRefEvent]
public record struct LitanyScryingEvent(EntityUid Caster, EntityUid Target, TimeSpan Duration, bool Handled);
