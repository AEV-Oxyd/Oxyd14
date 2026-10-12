using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.AstersGuild;

/// <summary>
/// Components for the Eris Asters Guild port (code/modules/trade, code/modules/mining,
/// code/modules/economy). Runtime trade state lives in OxydTradeSystem on the server.
/// </summary>

/// <summary>Eris preset/trade modular console: window into OxydTradeSystem.</summary>
[RegisterComponent]
public sealed partial class OxydTradeConsoleComponent : Component
{
    /// <summary>Guild account balance linked to this console (Eris account link).</summary>
    [DataField]
    public int Balance = 3000;

    /// <summary>Currently selected station index (-1 = tree view).</summary>
    public int SelectedStation = -1;

    /// <summary>Currently selected goods category.</summary>
    public string? Category;

    /// <summary>Cart: station goods index -> count.</summary>
    public Dictionary<int, int> Cart = new();

    /// <summary>Transaction log lines (Eris trade_log).</summary>
    public List<string> Log = new();
}

/// <summary>Eris receiving trade beacon: ordered goods materialize here.</summary>
[RegisterComponent]
public sealed partial class OxydTradeBeaconReceivingComponent : Component
{
}

/// <summary>Eris sending trade beacon: entities placed on/near it are exported for
/// offers and direct sales.</summary>
[RegisterComponent]
public sealed partial class OxydTradeBeaconSendingComponent : Component
{
}

/// <summary>Eris production machine console: drives the paired OxydOreProcessor.</summary>
[RegisterComponent]
public sealed partial class OxydOreProcessorConsoleComponent : Component
{
    /// <summary>The processor this console is paired to (nearest within 3 tiles, Eris
    /// processing_unit_console.LateInitialize).</summary>
    public EntityUid? Machine;
}

/// <summary>Eris processing_unit: melts ore stacks into material sheets, with alloy
/// recipes (plasteel etc.).</summary>
[RegisterComponent]
public sealed partial class OxydOreProcessorComponent : Component
{
    public bool Running;
    public string? LinkedOreType;
    /// <summary>Seconds per ore -> sheet cycle.</summary>
    [DataField]
    public float CycleSeconds = 5f;
    public float Accum;
    /// <summary>Storing toggles per material (Eris ORE_STORING).</summary>
    public Dictionary<string, bool> Storing = new();
    /// <summary>Ore items waiting to smelt.</summary>
    public List<string> Queue = new();
}

/// <summary>Eris deep_drill: slowly extracts ore stacks from the tile beneath.</summary>
[RegisterComponent]
public sealed partial class OxydDeepDrillComponent : Component
{
    public bool Running;
    [DataField]
    public float OreEvery = 10f;
    public float Accum;
}

/// <summary>Eris cash register (browse window — stock SS14 UI on the port side).</summary>
[RegisterComponent]
public sealed partial class OxydCashRegisterComponent : Component
{
    public int Stored;
}

/// <summary>Eris EFTPOS terminal (browse window — stock UI on the port side).</summary>
[RegisterComponent]
public sealed partial class OxydEftposComponent : Component
{
    [DataField]
    public int Charge = 0;
}

/// <summary>Visual-state keys for Asters Guild machine sprites.</summary>
[Serializable, NetSerializable]
public enum OxydGuildVisuals : byte
{
    Working,
}

/// <summary>Visual layers used by Asters Guild machine sprites.</summary>
[Serializable, NetSerializable]
public enum OxydGuildVisualLayers : byte
{
    Base,
    Working,
}
