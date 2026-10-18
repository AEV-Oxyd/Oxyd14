using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Technomancer;

/// <summary>
/// Eris RCON-style remote power console — lists every SMES and breaker box on
/// the map with remote toggle controls (Eris rcon_console.dm; the stock
/// PowerMonitoringConsole only reads values, so the write path is ours).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydRconComponent : Component;

/// <summary>
/// Eris atmos_control modular program as a fixed console — lists every air
/// alarm with area + status (Eris atmos_control.tmpl, fixed-console Adapt
/// per blueprint §7/§11).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydAtmosConsoleComponent : Component;

/// <summary>
/// Eris /obj/machinery/autolathe/nanoforge — consumes items into compressed
/// matter (autolathe.dm; 0.6 efficiency, 240 storage).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydNanoforgeComponent : Component
{
    [DataField]
    public int StoredMatter;

    [DataField]
    public int Capacity = 240;
}

/// <summary>
/// Eris tool mod (tools/mods) Adapt — a single-use mod item that grants a
/// ToolQuality to a tool it is used on (stock semantics: permanent quality
/// patch, Eris' deeper mod economy gap-doc'd).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydToolModComponent : Component
{
    [DataField(required: true)]
    public ProtoId<Content.Shared.Tools.ToolQualityPrototype> GrantsQuality;
}

/// <summary>Marker applied to a tool patched by an OxydToolMod.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydToolModdedComponent : Component
{
    [DataField]
    public List<string> AppliedQualities = new();
}

/// <summary>
/// Eris rapid pipe dispenser Adapt — use in hand to dispense a pipe segment
/// entity at your feet (IJIRO disk RPD; stock RapidPipeDispenser doesn't
/// exist in this codebase so a minimal single-proto dispenser fills the slot).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydRpdComponent : Component
{
    [DataField]
    public EntProtoId PipeProto = "GasPipeStraight";
}

/// <summary>
/// Eris engineering design disk Adapt (technomancers.dm ARK/IJIRO/ESPO/LAT
/// disks) — carrying a set of design descriptions; Use in hand reads them
/// out. Oxyd's research/lathe model doesn't license-by-disk, so the disks
/// are self-describing reference items.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OxydDesignDiskComponent : Component
{
    [DataField]
    public List<string> Designs = new();
}

[Serializable, NetSerializable]
public enum OxydRconUiKey : byte { Key }

[Serializable, NetSerializable]
public enum OxydAtmosUiKey : byte { Key }

[Serializable, NetSerializable]
public enum OxydForgeUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class OxydRconUiState : BoundUserInterfaceState
{
    public List<RconRow> Rows = new();
}

[Serializable, NetSerializable]
public sealed class RconRow
{
    public NetEntity Entity;
    public string Name = "";
    public string Group = "";      // "SMES" or "BREAKER"
    public int ChargePercent;      // SMES only
    public bool InputEnabled;
    public bool OutputEnabled;
}

[Serializable, NetSerializable]
public sealed class OxydRconToggleMessage : BoundUserInterfaceMessage
{
    public NetEntity Entity;
    public string Field = "";      // "input" | "output" | "breaker"
    public bool Value;
}

[Serializable, NetSerializable]
public sealed class OxydAtmosUiState : BoundUserInterfaceState
{
    public List<AtmosRow> Rows = new();
}

[Serializable, NetSerializable]
public sealed class AtmosRow
{
    public NetEntity Entity;
    public string Name = "";
    public string Area = "";
    public string Status = "";
}

[Serializable, NetSerializable]
public sealed class OxydForgeUiState : BoundUserInterfaceState
{
    public int StoredMatter;
    public int Capacity;
}

[Serializable, NetSerializable]
public sealed class OxydForgeEjectMessage : BoundUserInterfaceMessage;
