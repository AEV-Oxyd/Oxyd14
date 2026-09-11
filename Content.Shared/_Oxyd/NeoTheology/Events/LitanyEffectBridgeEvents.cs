using Content.Shared.Chemistry.Reagent;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared.Stacks;
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
/// Bridge for Rejection: raised on the body; the server removes every non-cruciform implant
/// and damages the body, the closest fork equivalent of Eris shedding foreign matter.
/// </summary>
[ByRefEvent]
public record struct LitanyRejectForeignBodyEvent(EntityUid Target, bool Handled);

/// <summary>
/// Bridge for Reveal Adversaries: raised on the caster; the server scans hostile fauna and
/// landmines and sends the Eris messages.
/// </summary>
[ByRefEvent]
public record struct LitanyRevealAdversariesEvent(EntityUid User, bool Handled);

/// <summary>
/// Bridge for Words of Purging: raised on the target; the server purges the listed reagents
/// from the target's bloodstream.
/// </summary>
[ByRefEvent]
public record struct LitanyPurgeAddictionEvent(EntityUid Target, IReadOnlyList<ProtoId<ReagentPrototype>> Reagents, bool Handled);

/// <summary>
/// Bridge for Atonement and Penance: raised on the target; the server applies the Eris pain
/// rider. The fork has no nonphysical pain value, so this maps to stamina damage.
/// </summary>
[ByRefEvent]
public record struct LitanyPainEvent(EntityUid Target, float Amount, bool Handled);

/// <summary>
/// Bridge for Asacris: raised on the body; the server strips every upgrade from the installed
/// cruciform.
/// </summary>
[ByRefEvent]
public record struct LitanyRemoveUpgradesEvent(EntityUid Target, bool Handled);

/// <summary>
/// Bridge for Accelerated Growth: raised on the caster; the server applies the growth boost to
/// every plant in range.
/// </summary>
[ByRefEvent]
public record struct LitanyAcceleratedGrowthEvent(
    EntityUid User,
    float Multiplier,
    TimeSpan Duration,
    bool Handled);

/// <summary>
/// Bridge for BaptismalRecord: raised on the NeoTheology altar; the server <c>AltarSystem</c>
/// spawns a paper listing the active cruciform bearers and sets <see cref="Handled"/>.
/// </summary>
[ByRefEvent]
public record struct LitanyBaptismalRecordEvent(EntityUid Altar, bool Handled);

/// <summary>
/// Bridge for Resurrection: raised on the NeoTheology cloner among the litany's machine targets;
/// the server <c>CruciformReaderSystem</c> reads the soul out of <see cref="Reader"/>, starts
/// a pod job from the saved body profile. Validation checks the soul, session, machines, and biomatter without changing them.
/// </summary>
[ByRefEvent]
public record struct LitanyResurrectionEvent(EntityUid Cloner, EntityUid Reader, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for MakeCruciform: raised on the NeoTheology forge among the litany's machine targets;
/// the server <c>CruciformForgeSystem</c> starts the forge's own produce run and sets
/// <see cref="Handled"/>. A false <see cref="Handled"/> means the forge was already working or
/// short of its recipe.
/// </summary>
[ByRefEvent]
public record struct LitanyForgeProduceEvent(EntityUid Forge, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for RepairDoor: raised on the holy door; the server <c>NeoTheologyDoorSystem</c>
/// heals it and burns <paramref name="User"/>'s biomatter, then sets <see cref="Handled"/>.
/// A false <see cref="Handled"/> means the door was undamaged or no biomatter was in reach.
/// </summary>
[ByRefEvent]
public record struct LitanyRepairDoorEvent(EntityUid Door, EntityUid User, bool Handled, bool ValidateOnly = false);

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
public record struct LitanyPumpBioreactorEvent(EntityUid Bioreactor, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for BioreactorChamber: raised on the bioreactor; the server <c>BioreactorSystem</c>
/// opens or shuts the chamber door and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means the door was still jammed or the chamber still held solution.
/// </summary>
[ByRefEvent]
public record struct LitanyToggleBioreactorChamberEvent(EntityUid Bioreactor, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for Scrying: raised on the scried body; the server <c>ScryingSystem</c> binds
/// <see cref="Caster"/>'s eye to <see cref="Target"/>'s surroundings for <see cref="Duration"/>
/// and sets <see cref="Handled"/>. A false <see cref="Handled"/> means the caster had no eye or
/// already had a live session.
/// </summary>
[ByRefEvent]
public record struct LitanyScryingEvent(EntityUid Caster, EntityUid Target, TimeSpan Duration, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for the offering litanies (DivineIntervention, HolyGuidance): raised on the Eye of the
/// Protector; the server <c>AltarSystem</c> collects <see cref="OfferingKey"/>'s requirements from
/// the altar within <see cref="User"/>'s reach, banks the observation on the Eye and sets
/// <see cref="Handled"/>. A false <see cref="Handled"/> means there was no altar or the offering
/// was under-stocked.
/// </summary>
[ByRefEvent]
public record struct LitanyOfferingEvent(EntityUid User, string OfferingKey, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for OrderArmaments: raised on the armaments printer; the server
/// <c>ArmamentsPrinterSystem</c> opens its own shop UI for <see cref="User"/> and sets
/// <see cref="Handled"/>. A false <see cref="Handled"/> means the UI could not be opened.
/// </summary>
[ByRefEvent]
public record struct LitanyOpenArmamentsEvent(EntityUid User, bool Handled, bool ValidateOnly = false);

/// <summary>
/// Bridge for Initiation: raised on the target follower; the server <c>CruciformSystem</c>
/// installs the preacher-convert core module when absent, activates it (converting the bearer to
/// the module's activation profile) and sets <see cref="Handled"/>. A false <see cref="Handled"/>
/// means the target had no active cruciform or was already a preacher.
/// </summary>
[ByRefEvent]
public record struct LitanyInitiationEvent(EntityUid User, bool Handled);

/// <summary>
/// Bridge for DivineGuidance: raised on the caster; the server
/// <c>NeoTheologyConstructionSystem</c> prints the chosen blueprint's material list to the caster
/// and sets <see cref="Handled"/>. A false <see cref="Handled"/> means the blueprint id is unknown.
/// </summary>
[ByRefEvent]
public record struct LitanyBlueprintInfoEvent(
    EntityUid User,
    ProtoId<NeoTheologyBlueprintPrototype> Blueprint,
    bool Handled,
    LocId? Failure = null);

/// <summary>
/// Bridge for Manifestation: raised on the caster; the server
/// <c>NeoTheologyConstructionSystem</c> checks the materials lying on the tile in front of the
/// caster, spends them and raises the structure. <see cref="ValidateOnly"/> must not mutate.
/// </summary>
[ByRefEvent]
public record struct LitanyManifestationEvent(
    EntityUid User,
    ProtoId<NeoTheologyBlueprintPrototype> Blueprint,
    bool ValidateOnly,
    bool Handled,
    LocId? Failure = null);

/// <summary>
/// Bridge for Uproot: raised on the caster; the server <c>NeoTheologyConstructionSystem</c>
/// finds the blueprint construct on the tile in front of the caster, returns its materials and
/// deletes it. <see cref="ValidateOnly"/> must not mutate.
/// </summary>
[ByRefEvent]
public record struct LitanyUprootEvent(
    EntityUid User,
    bool ValidateOnly,
    bool Handled,
    LocId? Failure = null);

/// <summary>
/// Bridge for Knowledge: raised on the caster; the server <c>NtUplinkSystem</c> reads the hidden
/// uplink's telecrystals and shows the count. <see cref="ValidateOnly"/> must not create the
/// store or send the message; it only checks that the uplink module is present.
/// </summary>
[ByRefEvent]
public record struct LitanyUplinkReportEvent(EntityUid User, bool ValidateOnly, bool Handled);

/// <summary>
/// Bridge for Bounty: raised on the caster; the server <c>NtUplinkSystem</c> creates the hidden
/// uplink on demand and opens its interface. <see cref="ValidateOnly"/> only checks that the
/// uplink module is present.
/// </summary>
[ByRefEvent]
public record struct LitanyUplinkOpenEvent(
    EntityUid User,
    bool ValidateOnly,
    bool Handled,
    LocId? Failure = null);
