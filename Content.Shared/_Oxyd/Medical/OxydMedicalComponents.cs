using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Time at which the mob entered the Dead state (Eris timeofdeath). Added on death and
/// removed on revive; a missing record is treated as "unknown -> allow" by the resuscitator.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class OxydTimeOfDeathComponent : Component
{
    [AutoPausedField]
    public TimeSpan DiedAt;
}

/// <summary>
/// Nervous System Accumulation (Eris NSA). Reagents contribute load; exceeding the threshold
/// inflicts toxin damage until the load subsides. Eris tracks contribution per reagent datum;
/// here the server system rebuilds contributions each tick from blood reagent metabolism data.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class OxydNsaComponent : Component
{
    /// <summary>Eris: nerve_system_accumulation threshold at which overload effects start.</summary>
    [DataField, AutoNetworkedField]
    public float Threshold = 100f;

    /// <summary>Current NSA load (sum of reagent contributions minus modifiers).</summary>
    [DataField, AutoNetworkedField]
    public float Current;

    /// <summary>Temporary threshold bonus from detox nanites/drugs (Eris "tolerance").</summary>
    [DataField, AutoNetworkedField]
    public float ToleranceBonus;

    [DataField, AutoNetworkedField]
    public TimeSpan ToleranceUntil;

    /// <summary>Toxin damage applied per second while over the threshold (Eris overload).</summary>
    [DataField]
    public float OverloadToxinPerSecond = 1.5f;

    /// <summary>Seconds between checks.</summary>
    [DataField]
    public float UpdateInterval = 1f;

    [AutoPausedField]
    public TimeSpan NextUpdate;
}

/// <summary>Sleeper: stasis bed with a fixed injection menu (Eris Sleeper.dm). Bed occupancy uses the
/// standard machine container; chems are injected straight into the occupant's bloodstream.</summary>
[RegisterComponent]
public sealed partial class OxydSleeperComponent : Component
{
    public static readonly string BodyContainerId = "oxyd_sleeper_body";
    public static readonly string BeakerContainerId = "oxyd_sleeper_beaker";

    /// <summary>Eris sleeper chem menu (6 slots).</summary>
    [DataField]
    public List<OxydSleeperChemEntry> Chems = new();

    /// <summary>Ensures an occupant (Eris checks stat(CONSCIOUS) for injection warnings).</summary>
    [DataField]
    public float MaxChemInPatient = 50f;

    /// <summary>Eris dialysis (Sleeper.dm filtering): while on, every tick moves DialysisRate
    /// units of each bloodstream reagent into the loaded beaker. Requires an occupant and a
    /// beaker; auto-stops when the beaker fills or is removed.</summary>
    [DataField]
    public float DialysisRate = 3f;

    /// <summary>Whether dialysis is currently running (Eris `filtering`).</summary>
    public bool Filtering;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class OxydSleeperChemEntry
{
    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public string? Name;

    [DataField]
    public float Dose = 10f;

    /// <summary>Max units in the patient before the button disables (Eris stasis chems cap).</summary>
    [DataField]
    public float MaxInPatient = 50f;
}

/// <summary>Autodoc: automatic surgeon pod (Eris machinery/autodoc.dm + surgery/autodoc.dm's
/// capitalist_autodoc). Scan builds patchnotes of detected problems; the user toggles operations
/// per organ, pays in inserted credits, and the pod processes them one operation per step.
/// Eris charges personal bank accounts; SS14 has no per-person banking, so the balance is
/// physical SpaceCash inserted into the machine's credit slot.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydAutodocComponent : Component
{
    public static readonly string BodyContainerId = "oxyd_autodoc_body";
    public static readonly string CreditsContainerId = "oxyd_autodoc_credits";

    /// <summary>Seconds per operation tick (Eris capitalist processing_speed = 20s; shortened
    /// for SS14 round pacing).</summary>
    [DataField]
    public float StepDuration = 8f;

    /// <summary>Damage healed / units pumped per operation tick (Eris damage_heal_amount).</summary>
    [DataField]
    public float HealPerTick = 20f;

    /// <summary>Bloodstream units purged per dialysis tick (Eris AUTODOC_DIALYSIS_AMOUNT).</summary>
    [DataField]
    public float DialysisPerTick = 5f;

    /// <summary>Eris cost defines (surgery/autodoc.dm).</summary>
    [DataField]
    public int ScanCost = 200;

    [DataField]
    public Dictionary<OxydAutodocOp, int> OpCosts = new()
    {
        [OxydAutodocOp.Damage] = 800,
        [OxydAutodocOp.Shrapnel] = 1000,
        [OxydAutodocOp.Fracture] = 1200,
        [OxydAutodocOp.OpenWounds] = 600,
        [OxydAutodocOp.InternalWounds] = 1200,
        [OxydAutodocOp.Blood] = 800,
        [OxydAutodocOp.Toxin] = 600,
        [OxydAutodocOp.Dialysis] = 1000,
    };

    /// <summary>Whether the pod is mid-procedure (named to avoid hiding
    /// <see cref="Component.Running"/>).</summary>
    [DataField, AutoNetworkedField]
    public bool Operating;

    /// <summary>Current scan results; Organ == null is the global toxnote.</summary>
    public List<OxydAutodocPatchnote> Notes = new();

    /// <summary>Ops selected when the run started (progress denominator).</summary>
    public int OpsTotal;

    public TimeSpan NextOpTime;
}

/// <summary>One Eris autodoc_patchnote: operations detected (Scanned) and selected (Picked)
/// on a single organ — or globally when Organ is null.</summary>
public sealed class OxydAutodocPatchnote
{
    public EntityUid? Organ;
    public HashSet<OxydAutodocOp> Scanned = new();
    public HashSet<OxydAutodocOp> Picked = new();
}

/// <summary>Centrifuge/electrolyzer (Eris machinery/centrifuge.dm, electrolyzer.dm).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydChemProcessorComponent : Component
{
    public static readonly string MainBeakerId = "oxyd_processor_main";
    public static readonly string SepBeakerIdPrefix = "oxyd_processor_sep_";
    public const int SeparationBeakerCount = 3;

    [DataField]
    public OxydChemProcessorMode Mode = OxydChemProcessorMode.Centrifuge;

    /// <summary>reagent id -> separation beaker index (0-2), -1 leaves it in the main beaker.</summary>
    [DataField]
    public Dictionary<string, int> Targets = new();

    [DataField, AutoNetworkedField]
    public bool Working;

    /// <summary>Eris electrolyzer: when on, restarts the work cycle automatically
    /// while there is still a decomposable reagent in the main beaker.</summary>
    [DataField, AutoNetworkedField]
    public bool Continuous;

    [DataField]
    public float WorkDuration = 5f;

    public TimeSpan WorkEnd;
}

/// <summary>IV drip (Eris machinery/iv_drip.dm): transfers blood between patient and attached beaker/bloodpack.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class OxydIvDripComponent : Component
{
    public static readonly string BeakerContainerId = "oxyd_iv_beaker";

    /// <summary>Eris "Set IV transfer amount" verb: cycles through these units-per-tick values.</summary>
    public static readonly float[] TransferRates = { 1f, 3f, 5f, 8f, 10f };

    [DataField, AutoNetworkedField]
    public NetEntity? AttachedTo;

    [DataField, AutoNetworkedField]
    public float TransferPerTick = 5f;

    [DataField]
    public float TickInterval = 2f;

    /// <summary>Seconds the attach do-after takes (Eris attach has no delay; SS14
    /// injections require one, like <see cref="Content.Shared.Chemistry.Components.InjectorComponent"/>).</summary>
    [DataField]
    public float AttachDelay = 2f;

    [AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>Whether to draw blood out of the patient instead of injecting.</summary>
    [DataField, AutoNetworkedField]
    public bool DrainMode;
}

/// <summary>Stasis bag (Eris stasis_bag.dm): one-use bodybag that halts a patient's deterioration.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydStasisBagComponent : Component
{
    public static readonly string BodyContainerId = "oxyd_stasis_body";

    /// <summary>Eris: bags are non-reusable.</summary>
    [DataField]
    public bool OneUse = true;
}

/// <summary>Flag on the patient inside a stasis bag while folded in.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydInStasisComponent : Component
{
}

/// <summary>Surgical operating table (Eris operating_table) marker for flavor/bonus hooks.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydOperatingTableComponent : Component
{
}

/// <summary>Organ preservation container (Eris organ_freezer) – slows organ damage while inside.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydOrganFreezerComponent : Component
{
}

/// <summary>
/// Marks a container/entity that suspends organ decay for organs stored inside
/// (Eris is_in_stasis: organ_freezer, cryobag, smartfridge...).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydOrganStasisComponent : Component
{
}

/// <summary>A reusable morgue tray marker.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydMorgueTrayComponent : Component
{
}

/// <summary>Marker on the Eris-style scanner item (health analyzer sprite w/ organ table).
/// Referenced by item prototypes, so it lives in Shared.</summary>
[RegisterComponent]
public sealed partial class OxydScannerItemComponent : Component
{
}

/// <summary>Eris organ bioprinter: stores biomass, prints replacement organs.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class OxydBioprinterComponent : Component
{
    /// <summary>Stored biomass (Eris stored_matter). Round-start preload mirrors Eris (200).</summary>
    [DataField]
    public int StoredMatter = 200;

    [DataField]
    public int MaxMatter = 300;

    /// <summary>A blood sample was injected (Eris loaded_dna). Cosmetic parity flag.</summary>
    [DataField]
    public bool HasBloodSample;

    /// <summary>Busy printing an organ.</summary>
    [DataField]
    public bool Working;

    /// <summary>When the current print finishes (Eris working animation window).</summary>
    [AutoPausedField]
    public TimeSpan NextFinish;
}

/// <summary>Eris appendix.dm: spontaneous appendicitis - inflamed counter ticks
/// up while inside a body, escalating pain/vomit until the organ is removed
/// or ruptures. Inflamed is a counter, not a boolean.</summary>
[RegisterComponent]
public sealed partial class OxydAppendixComponent : Component
{
    /// <summary>Seconds of inflammation elapsed (Eris inflamed counter, 1/lifetick).</summary>
    public float Inflamed;
}

/// <summary>Autopsy scanner (Eris objects/items/weapons/autopsy.dm): handheld item used
/// on a cadaver; after a scan it prints a paper autopsy report with the body's wounds
/// and bloodstream contents.</summary>
[RegisterComponent]
public sealed partial class OxydAutopsyScannerComponent : Component
{
    /// <summary>Seconds the post-mortem scan takes. Eris scans one limb at a time; the
    /// SS14 report samples the whole body in a single pass.</summary>
    [DataField]
    public float ScanDelay = 4f;
}

/// <summary>Mass spectrometer (Eris devices/scanners/mass_scpectrometer.dm): handheld
/// scanner that prints the reagent mix of a container or a patient's bloodstream.</summary>
[RegisterComponent]
public sealed partial class OxydMassSpectrometerComponent : Component
{
    /// <summary>Eris `details`: the advanced model also reports reagent amounts.</summary>
    [DataField]
    public bool Detailed;
}

/// <summary>Operating computer (Eris machinery/computer/Operating.dm): console showing the
/// vitals of the patient buckled to the closest operating table.</summary>
[RegisterComponent]
public sealed partial class OxydOperatingComputerComponent : Component
{
    /// <summary>Search radius for the linked operating table. Eris checks the four cardinal
    /// tiles; a short range also covers diagonal tables.</summary>
    [DataField]
    public float TableSearchRadius = 1.8f;
}
