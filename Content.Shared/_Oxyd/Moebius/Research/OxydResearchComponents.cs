// Eris Moebius research port: component declarations.
// Eris source: code/modules/research/{rdconsole,rdmachines,destructive_analyzer,experiment,research}.dm
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Oxyd.Moebius.Research;

/// <summary>The Moebius R&D control console (Eris /obj/machinery/computer/rdconsole).
/// Holds the department's research state: points, per-tree levels, unlocked nodes,
/// and the link list of analyzer/lathes/fabs it drives. Eris keeps this on
/// /datum/research attached to the console.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydRdConsoleComponent : Component
{
    /// <summary>Eris research_points: spent to unlock tech nodes.</summary>
    [DataField, AutoNetworkedField]
    public int Points;

    /// <summary>Eris researched_tech levels: raised by deconstruction and node unlocks.
    /// Keyed by oxydTechTree prototype id.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> TreeLevels = new();

    /// <summary>Unlocked oxydTechNode ids (Eris researched_nodes).</summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> UnlockedNodes = new();

    /// <summary>Devices linked to this console (Eris linked_* lists):
    /// destructive analyzers, protolathes, imprinters, mechfabs.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> LinkedDevices = new();

    /// <summary>Screen currently selected in the UI (Eris NanoUI screen var).</summary>
    [DataField, AutoNetworkedField]
    public int Screen;
}

/// <summary>Marker for machines the R&D console can link to
/// (Eris rdconsole auto-links analyzers/lathes/fabs within range).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydRdLinkableComponent : Component
{
    /// <summary>Human-readable device role shown in the console device list.</summary>
    [DataField, AutoNetworkedField]
    public OxydRdDeviceKind Kind = OxydRdDeviceKind.Protolathe;
}

[Serializable, NetSerializable]
public enum OxydRdDeviceKind : byte
{
    Analyzer,
    Protolathe,
    Imprinter,
    Mechfab,
}

/// <summary>Eris /obj/machinery/r_n_d/destructive_analyzer:
/// destroys a loaded item, converting its origin_tech into research levels/points
/// and refunding part of its materials.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydDestructiveAnalyzerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Busy;

    /// <summary>Server-side end time for the current deconstruction.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan BusyUntil;

    /// <summary>Eris deconstruction takes a few seconds.</summary>
    [DataField]
    public float DeconstructTime = 4f;

    /// <summary>Item mid-deconstruction (reviewer round-2: pending work lives on
    /// the component, not a system-level dictionary keyed by entity).</summary>
    public EntityUid? PendingItem;

    /// <summary>Console that ordered the running deconstruction.</summary>
    public EntityUid? PendingConsole;
}

/// <summary>Eris origin_tech on items: tech tree -> level this object advances
/// when deconstructed. Also what the science tool reads for value.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydOriginTechComponent : Component
{
    /// <summary>oxydTechTree proto id -> level granted on deconstruct.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Techs = new();
}

/// <summary>Eris /obj/item/device/science_tool: scans an object with origin_tech
/// and stores a data block the R&D console uploads for points.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydScienceToolComponent : Component
{
    /// <summary>Stored scan results awaiting upload (Eris datablocks).</summary>
    [DataField, AutoNetworkedField]
    public List<OxydScanBlock> Blocks = new();

    /// <summary>Eris tool also reports the research value of an object directly.</summary>
    [DataField, AutoNetworkedField]
    public int TotalValue;
}

[Serializable, NetSerializable]
public sealed class OxydScanBlock
{
    public string Subject = string.Empty;
    public int Value;
}


/// <summary>Eris autopsy report / experiment data block:
/// a scannable item worth points when fed to the console.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydAutopsyReportComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Report = string.Empty;

    [DataField, AutoNetworkedField]
    public int Value = 500;
}

/// <summary>Eris /obj/item/device/radio/beacon/explosion_watcher (KES):
/// banks research points when it registers an explosion while anchored.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydExplosionWatcherComponent : Component
{
    [DataField, AutoNetworkedField]
    public int BankedPoints;

    [DataField, AutoNetworkedField]
    public int ExplosionsRecorded;
}

/// <summary>Eris portable research disk (device/disk/tech_disk + design disk):
/// stores tech nodes and design ids the console can read/write.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydResearchDataDiskComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<string> StoredNodes = new();

    [DataField, AutoNetworkedField]
    public HashSet<string> StoredDesigns = new();
}

/// <summary>Eris /obj/item/computer_hardware/hard_drive/portable/research_points:
/// a loot disk that grants a flat point sum when loaded into the console.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydResearchPointsDiskComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Points = 5000;

    [DataField, AutoNetworkedField]
    public bool Spent;
}

/// <summary>Eris /obj/item/device/mental_imprinter (spawn_blacklisted loot):
/// the user stabs it into their eye for a stat boost at heavy sanity cost.
/// Adapted: sanity damage + brief euphoria message; no stat system exists
/// in the fork so the boost is flavor.</summary>
[RegisterComponent]
public sealed partial class OxydMentalImprinterComponent : Component
{
    /// <summary>Eris sanity toll for the imprint.</summary>
    [DataField] public float SanityDamage = 80f;
}

/// <summary>Eris /obj/machinery/drone_fabricator: builds maintenance drone
/// shells over time. Ported-with-blocker: the fork has no inhabitable silicon
/// drone stack, so it produces deployable shells that unfold into a
/// non-player NPC drone.</summary>
[RegisterComponent]
public sealed partial class OxydDroneFabricatorComponent : Component
{
    /// <summary>Item produced on each fabrication cycle.</summary>
    [DataField] public EntProtoId ShellProto = "OxydMaintenanceDroneShell";

    /// <summary>Seconds between fabrications (Eris takes minutes; shortened for playtests).</summary>
    [DataField] public float CooldownSeconds = 30f;

    /// <summary>Game-time when the next shell may be produced.</summary>
    public TimeSpan NextUse;
}

/// <summary>Eris xenobiology extract tooling: draws a core out of a
/// refinable biological specimen (slimes in the fork). The cores are
/// scannable/deconstructable research items.</summary>
[RegisterComponent]
public sealed partial class OxydSlimeExtractorComponent : Component
{
    /// <summary>Entity spawned per extraction.</summary>
    [DataField] public EntProtoId CoreProto = "OxydSlimeCore";

    /// <summary>Remaining extractions before the tool empties.</summary>
    [DataField] public int Uses = 3;
}

/// <summary>Marker for the fold-out maintenance drone shell item: activates
/// into the NPC drone (Eris maint drone; can't be inhabited without silicon).</summary>
[RegisterComponent]
public sealed partial class OxydMaintenanceDroneShellComponent : Component
{
    /// <summary>Mob the shell unfolds into.</summary>
    [DataField] public EntProtoId DroneProto = "OxydMaintenanceDrone";
}
