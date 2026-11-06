using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Silicons;

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

/// <summary>Marker for the fold-out maintenance drone shell item: activates
/// into the NPC drone (Eris maint drone; can't be inhabited without silicon).</summary>
[RegisterComponent]
public sealed partial class OxydMaintenanceDroneShellComponent : Component
{
    /// <summary>Mob the shell unfolds into.</summary>
    [DataField] public EntProtoId DroneProto = "OxydMaintenanceDrone";
}
