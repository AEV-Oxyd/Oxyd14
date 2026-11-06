using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Shared UI messages for the ported Eris medical machinery (Moebius).</summary>

// ---------------- Surgery ----------------
// Ports Eris nano_ui/modules/moebius/surgery. The UI proxy entity is spawned in
// nullspace by the server so no component has to be added to patient prototypes.

[Serializable, NetSerializable]
public enum OxydSurgeryUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydSurgeryOrganEntry
{
    public NetEntity Organ;
    public string Name = string.Empty;
    public bool Robotic;
    public bool External;
    public OxydIncisionStage Incision;
    public bool Clamped;
    public bool Fractured;
    public bool Splinted;
    public float OrganDamage;
    /// <summary>Brute damage on this organ (Eris brute_dam, separate Diagnostics bar).</summary>
    public float BruteDamage;
    /// <summary>Burn damage on this organ (Eris burn_dam, separate Diagnostics bar).</summary>
    public float BurnDamage;
    public int EmbeddedCount;
    /// <summary>Wound details were diagnosed (scanner / wound probe). Eris hides them until then.</summary>
    public bool Diagnosed = true;
    /// <summary>Display cap for the organ's health bar (Eris organ.max_damage).</summary>
    public float MaxDamage = OxydOrganSurgeryComponent.OrganMaxDamage;
    /// <summary>Maximum cavity implants (Eris limb.max_volume).</summary>
    public int CavityMax = OxydOrganSurgeryComponent.ImplantCavityMax;
    /// <summary>Steps the held tool can start on this organ right now.</summary>
    public List<OxydSurgeryStep> AvailableSteps = new();
    /// <summary>Step currently running on this organ, if any.</summary>
    public OxydSurgeryStep? RunningStep;

    /// <summary>Eris organ.is_open(): surgical site exposed (retracted incision, or open robo panel).
    /// Condition/action links are disabled until this is true.</summary>
    public bool Open;
    /// <summary>Limb efficiency percentage (Eris limb_efficiency, shown green in Diagnostics).</summary>
    public float Efficiency;
    /// <summary>Organ type names for the pink types line (Eris organ processes; externals show
    /// Bone/Muscle/Nerves, internals their own name). Rendered as "type (efficiency%)".</summary>
    public List<string> Processes = new();
    /// <summary>Blood stored by this organ's share of the body's bloodstream
    /// (Eris organ.current_blood / max_blood_storage).</summary>
    public float StoredBlood;
    public float MaxBlood;
    /// <summary>Number of internal wounds (Eris wounddatums length, orange "Wounds:" count).</summary>
    public int WoundCount;
    /// <summary>Wound cards for the internal view (Eris diag_wounds: type/severity/treatments).</summary>
    public List<OxydSurgeryWoundEntry> Wounds = new();
    /// <summary>Names of implanted/cavity items for the Modifications panel (Eris diag_mods).</summary>
    public List<string> ModNames = new();
    /// <summary>Show an Oxygen bar on this card (Eris respiratory/brain organs).</summary>
    public bool ShowOxygen;
}

/// <summary>One wound row on the internal view's Wounds panel (Eris diag_wounds entry).</summary>
[Serializable, NetSerializable]
public sealed class OxydSurgeryWoundEntry
{
    /// <summary>Wound type name (Eris wound.name).</summary>
    public string Name = string.Empty;
    /// <summary>Current severity and cap (Eris wound.severity / severity_max).</summary>
    public int Severity;
    public int SeverityMax;
    /// <summary>What can treat it (Eris wound.treatments text).</summary>
    public string Treatments = string.Empty;
    /// <summary>The surgery step that treats this wound, if one exists and the held tool can run it.</summary>
    public OxydSurgeryStep? FixStep;
}

[Serializable, NetSerializable]
public sealed class OxydSurgeryState : BoundUserInterfaceState
{
    public string PatientName = string.Empty;
    public OxydSurgeryTool HeldTools = OxydSurgeryTool.None;
    /// <summary>Display name of the item actually being used (not always a surgical tool).</summary>
    public string HeldItemName = string.Empty;
    /// <summary>Patient isn't on an operating surface: only surface steps are possible (Eris CAN_OPERATE_STANDING).</summary>
    public bool StandingOnly;
    /// <summary>Operating on yourself: heavier failure penalty (Eris self-surgery).</summary>
    public bool SelfSurgery;
    /// <summary>Patient's oxy loss for the Oxygen bars (Eris owner_oxyloss; bar shows oxymax-oxyloss).</summary>
    public float OwnerOxyLoss;
    /// <summary>Oxygen bar max (Eris 100 - owner.total_oxygen_req).</summary>
    public float OwnerOxyMax = 100f;
    public List<OxydSurgeryOrganEntry> Organs = new();
}

[Serializable, NetSerializable]
public sealed class OxydSurgerySelectStepMessage : BoundUserInterfaceMessage
{
    public NetEntity Organ;
    public OxydSurgeryStep Step;

    public OxydSurgerySelectStepMessage(NetEntity organ, OxydSurgeryStep step)
    {
        Organ = organ;
        Step = step;
    }
}

/// <summary>Server -> a specific actor's surgery window (per-surgeon refresh on the
/// patient-hosted BUI, since the shared UI state can't carry per-actor data).</summary>
[Serializable, NetSerializable]
public sealed class OxydSurgeryStateMessage : BoundUserInterfaceMessage
{
    public OxydSurgeryState State = default!;
}

// ---------------- Sleeper ----------------
// Ports Eris machinery/Sleeper.dm: occupant pod that injects a limited chem menu.

[Serializable, NetSerializable]
public enum OxydSleeperUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydSleeperState : BoundUserInterfaceState
{
    public bool HasOccupant;
    public string OccupantName = string.Empty;
    public float OccupantHealth;
    public bool OccupantCritical;
    /// <summary>Eris occupied view: per-group damage for the Brute/Burn/Respiratory/Toxin bars.</summary>
    public float BruteLoss;
    public float BurnLoss;
    public float ToxinLoss;
    public float OxyLoss;
    /// <summary>Eris "Organ Health" row: 100 - worst organ damage fraction.</summary>
    public float OrganHealth;
    public bool Alive;
    /// <summary>Eris "Pulse" stat row (victim.get_pulse classification).</summary>
    public OxydPulse Pulse;
    /// <summary>(reagent id, display name, amount already in patient, units per dose, injectable now)</summary>
    public List<OxydSleeperChem> Chems = new();
    public float BeakerVolume;
    public float BeakerMaxVolume;
    public bool HasBeaker;
    /// <summary>Eris dialysis `filtering`: pumping bloodstream reagents into the beaker.</summary>
    public bool Filtering;
    /// <summary>Dialysis can be toggled (occupant + beaker loaded).</summary>
    public bool FilterAvailable;
}

[Serializable, NetSerializable]
public sealed class OxydSleeperChem
{
    public string Reagent = string.Empty;
    public string Name = string.Empty;
    public float InPatient;
    public float DoseSize = 10f;
    public bool Enabled = true;
    public string? DisabledReason;
}

[Serializable, NetSerializable]
public sealed class OxydSleeperInjectMessage : BoundUserInterfaceMessage
{
    public string Reagent = string.Empty;
    public float Dose;
}

[Serializable, NetSerializable]
public sealed class OxydSleeperEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydSleeperEjectBeakerMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydSleeperToggleFilterMessage : BoundUserInterfaceMessage
{
}

// ---------------- Autodoc ----------------
// Ports Eris machinery/autodoc.dm + surgery/autodoc.dm (capitalist_autodoc): scan detects
// patchnotes of problems, the user toggles operations, inserted credits pay per operation.

[Serializable, NetSerializable]
public enum OxydAutodocUiKey : byte
{
    Key,
}

/// <summary>Autodoc operations (Eris AUTODOC_* bitflags).</summary>
[Serializable, NetSerializable]
public enum OxydAutodocOp : byte
{
    Damage,          // brute/burn on an organ (AUTODOC_DAMAGE)
    OpenWounds,      // bandage/clamp/salve wounds, seal incisions (AUTODOC_OPEN_WOUNDS)
    InternalWounds,  // internal organ damage (AUTODOC_INTERNAL_WOUNDS)
    Fracture,        // mend fracture (AUTODOC_FRACTURE)
    Shrapnel,        // remove embedded objects (AUTODOC_EMBED_OBJECT)
    Toxin,           // anti-toxin chelation (AUTODOC_TOXIN)
    Dialysis,        // purge bloodstream reagents (AUTODOC_DIALYSIS)
    Blood,           // replenish blood volume (AUTODOC_BLOOD)
}

[Serializable, NetSerializable]
public sealed class OxydAutodocState : BoundUserInterfaceState
{
    public bool HasOccupant;
    public string OccupantName = string.Empty;
    public bool Running;
    /// <summary>Overall progress 0-1 (Eris displayBar over the queue).</summary>
    public float Progress;

    // Overall status bars (Eris overall_status).
    public float BruteLoss;
    public float BurnLoss;
    public float ToxinLoss;
    public float OxyLoss;
    public float BloodPercent;

    /// <summary>Credits loaded in the machine (Eris patient_account balance; here: SpaceCash).</summary>
    public int Balance;
    public int ScanCost;
    public int TotalCost;
    public int CustomCost;

    /// <summary>Global toxnote ops (organ == null entry rendered on top).</summary>
    public OxydAutodocEntry Global = new() { Name = "", Global = true };
    public List<OxydAutodocEntry> Organs = new();
    /// <summary>Operation costs for rendering the per-op links.</summary>
    public Dictionary<OxydAutodocOp, int> OpCosts = new();
}

/// <summary>One patchnote row: an organ (or the global toxnote) with available/picked ops.</summary>
[Serializable, NetSerializable]
public sealed class OxydAutodocEntry
{
    /// <summary>Index into the server's patchnote list (0 = global).</summary>
    public int Id;
    public string Name = string.Empty;
    public bool Global;
    /// <summary>Internal organ (Eris renders inner_damage only).</summary>
    public bool Internal;
    public float BruteDamage;
    public float BurnDamage;
    public float InnerDamage;
    public List<OxydAutodocOp> Available = new();
    public List<OxydAutodocOp> Picked = new();
}

[Serializable, NetSerializable]
public sealed class OxydAutodocScanMessage : BoundUserInterfaceMessage
{
}

/// <summary>Process every scanned operation (Eris 'full').</summary>
[Serializable, NetSerializable]
public sealed class OxydAutodocProcessAllMessage : BoundUserInterfaceMessage
{
}

/// <summary>Process only the picked operations (Eris 'picked').</summary>
[Serializable, NetSerializable]
public sealed class OxydAutodocProcessPickedMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydAutodocAbortMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydAutodocToggleMessage : BoundUserInterfaceMessage
{
    public int EntryId;
    public OxydAutodocOp Op;
}

[Serializable, NetSerializable]
public sealed class OxydAutodocEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydAutodocEjectCreditsMessage : BoundUserInterfaceMessage
{
}

// ---------------- MIRC (Moebius Internal Reagent Catalogue) ----------------
// Ports Eris modular_computers/.../medical/chem_catalog.dm: a PDA program that lists
// the Moebius reagents and how to mix them.

[Serializable, NetSerializable]
public sealed class OxydMircUiState : BoundUserInterfaceState
{
    public List<OxydMircEntry> Entries = new();
}

[Serializable, NetSerializable]
public sealed class OxydMircEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    /// <summary>Pre-formatted recipe lines, e.g. "20u Hydrogen + 10u Oxygen".</summary>
    public List<string> Recipes = new();

    // Eris catalog entry spec block (catalog_entry_reagent.tmpl).
    /// <summary>Reagent group shown as Eris "Type" (Add/Drug/Medicine...).</summary>
    public string Type = string.Empty;
    /// <summary>Physical phase at STP derived from melting/boiling points (Liquid/Solid/Gas).</summary>
    public string Phase = string.Empty;
    /// <summary>Substance color as #RRGGBB for the color swatch.</summary>
    public string ColorHex = "#FFFFFF";
    /// <summary>Bloodstream metabolism rate in u/s (Eris metabolism field).</summary>
    public float Metabolism;
    /// <summary>NSA contribution while metabolising (Eris nerve_system_accumulation). -1 = none.</summary>
    public float Nsa = -1f;
    /// <summary>Addiction threshold in units. -1 = non-addictive.</summary>
    public float AddictionThreshold = -1f;
    /// <summary>Addiction chance per metabolise. -1 = non-addictive.</summary>
    public float AddictionChance = -1f;
    /// <summary>Taste description (flavor name) or empty.</summary>
    public string Taste = string.Empty;
    /// <summary>Reactions this reagent takes part in as a reactant (Eris "Takes part in
    /// reactions"); each links back to the produced catalog entry.</summary>
    public List<OxydMircLink> UsedIn = new();
}

/// <summary>A catalog link: display text + the entry it jumps to (null = plain text).</summary>
[Serializable, NetSerializable]
public sealed class OxydMircLink
{
    public string Label = string.Empty;
    public string? EntryId;
}

/// <summary>Eris cryo cell: On/Off toggle for the pod (stock SS14 pods are always-on
/// once powered; the Eris cell is switched explicitly).</summary>
[Serializable, NetSerializable]
public sealed class OxydCryoPodPowerMessage : BoundUserInterfaceMessage
{
    public bool On;
}

/// <summary>Eris get_pulse() classification (PULSE_NONE/THREADY/NORM/irregular):
/// dead - none, critical - thready, damaged heart organ - irregular, else norm.</summary>
[Serializable, NetSerializable]
public enum OxydPulse
{
    None,
    Thready,
    Irregular,
    Norm,
}

// ---------------- Health scanner ----------------
// Ports Eris medical scanners producing an Eris-style readout (organs + wounds + vitals),
// on top of the native analyser flow.

[Serializable, NetSerializable]
public enum OxydScannerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydScannerState : BoundUserInterfaceState
{
    public bool HasScan;
    public string PatientName = string.Empty;
    public float Health;
    public float BruteLoss;
    public float BurnLoss;
    public float ToxinLoss;
    public float OxyLoss;
    /// <summary>Eris readout: "Overall Status: alive/critical/deceased".</summary>
    public bool Alive;
    public bool Critical;
    /// <summary>Eris readout: "Pulse : ..." line.</summary>
    public OxydPulse Pulse;
    /// <summary>Body temperature in Kelvin for the °C/°F line.</summary>
    public float Temperature;
    public float Pain;
    public float Nsa;
    public float BloodLevel;
    public float BloodMax;
    public List<OxydScannerOrgan> Organs = new();
}

[Serializable, NetSerializable]
public sealed class OxydScannerOrgan
{
    public string Name = string.Empty;
    public string Status = string.Empty;
    public float Damage;
    public bool Fractured;
    public bool Bleeding;
}

// ---------------- Chem processor (centrifuge + electrolyzer) ----------------
// Ports Eris machinery/centrifuge.dm (isolate reagents into separation beakers)
// and machinery/electrolyzer.dm (apply power to trigger decomposition reactions).

[Serializable, NetSerializable]
public enum OxydChemProcessorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum OxydChemProcessorMode : byte
{
    Centrifuge,
    Electrolyzer,
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorState : BoundUserInterfaceState
{
    public OxydChemProcessorMode Mode;
    public bool Working;
    /// <summary>Eris centrifuge: selectable spin-cycle duration in seconds (5/10/15/30/60).</summary>
    public float Duration;
    /// <summary>Eris electrolyzer: "On" toggle keeps processing until switched off.</summary>
    public bool Continuous;
    public bool HasMainBeaker;
    public List<OxydChemProcessorReagent> MainContents = new();
    /// <summary>Centrifuge only: up to 3 separation beakers.</summary>
    public List<OxydChemProcessorBeaker> SeparationBeakers = new();
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorReagent
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public float Volume;
    /// <summary>Centrifuge: which beaker it should be isolated into (0-2), -1 = leave.</summary>
    public int TargetBeaker = -1;
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorBeaker
{
    public bool Present;
    public float Volume;
    public float MaxVolume;
    public List<OxydChemProcessorReagent> Contents = new();
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorSetTargetMessage : BoundUserInterfaceMessage
{
    public string Reagent = string.Empty;
    public int TargetBeaker;
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorStartMessage : BoundUserInterfaceMessage
{
}

/// <summary>Eris centrifuge: pick the spin-cycle duration (seconds).</summary>
[Serializable, NetSerializable]
public sealed class OxydChemProcessorSetDurationMessage : BoundUserInterfaceMessage
{
    public float Seconds;
}

/// <summary>Eris electrolyzer: switch the unit On (continuous processing) or Off.</summary>
[Serializable, NetSerializable]
public sealed class OxydChemProcessorSetRunningMessage : BoundUserInterfaceMessage
{
    public bool Running;
}

[Serializable, NetSerializable]
public sealed class OxydChemProcessorEjectMessage : BoundUserInterfaceMessage
{
    /// <summary>-1 = main beaker, otherwise separation beaker index.</summary>
    public int BeakerIndex;
}

// ---------------- Autopsy scanner ----------------
// Ports Eris autopsy.dm: a full-cadaver scan do_after ends in a printed paper report.

[Serializable, NetSerializable]
public sealed partial class OxydAutopsyDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>Attach do-after for the IV drip (Eris attach is instant; SS14 needs a timed
/// action like injectors use).</summary>
[Serializable, NetSerializable]
public sealed partial class OxydIvAttachDoAfterEvent : SimpleDoAfterEvent
{
}

// ---------------- Operating computer ----------------
// Ports Eris machinery/computer/Operating.dm: live vitals of the op-table patient.

[Serializable, NetSerializable]
public enum OxydOperatingComputerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydOperatingComputerState : BoundUserInterfaceState
{
    public bool HasPatient;
    public string PatientName = string.Empty;
    /// <summary>Alive & conscious (Eris "Stable"); critical reads as Non-Responsive.</summary>
    public bool Alive;
    public bool Critical;
    /// <summary>Eris "Critical Health" percentage.</summary>
    public float HealthPercent;
    /// <summary>Eris "Organ Health" percentage (worst external organ).</summary>
    public float OrganHealth;
    public float BruteLoss;
    public float BurnLoss;
    public float ToxinLoss;
    public float OxyLoss;
    /// <summary>Patient has a readable bloodstream.</summary>
    public bool HasBlood;
    /// <summary>Blood volume as a percentage of the patient's max (BloodstreamSystem.GetBloodLevel).</summary>
    public float BloodPercent;
    public float BloodVolume;
    /// <summary>Eris get_pulse classification.</summary>
    public OxydPulse Pulse;
}

/// <summary>Visual-state keys for Moebius machine sprites (occupied pod, running autodoc/processor).</summary>
[Serializable, NetSerializable]
public enum OxydMachineVisuals : byte
{
    Occupied,
    Working,
}

/// <summary>Visual layers used by Moebius machine sprites.</summary>
[Serializable, NetSerializable]
public enum OxydMachineVisualLayers : byte
{
    Base,
    Occupied,
}

// ---------------- Bioprinter (Eris machinery/bioprinter.dm) ----------------
// Prints replacement internal organs from stored biomass; a blood sample
// tags the print with donor data (kept as a flag - SS14 has no DNA system).

[Serializable, NetSerializable]
public enum OxydBioprinterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydBioprinterState : BoundUserInterfaceState
{
    public int StoredMatter;
    public int MaxMatter;
    public bool HasBloodSample;
    public bool Working;
    public List<OxydBioprinterProduct> Products = new();
}

[Serializable, NetSerializable]
public sealed class OxydBioprinterProduct
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Cost;
    public bool Affordable;
}

[Serializable, NetSerializable]
public sealed class OxydBioprinterPrintMessage : BoundUserInterfaceMessage
{
    public string Product = string.Empty;
}
