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
    /// <summary>Eris preset/trade_orders: crew-side console — Order Requests tab gains a
    /// submit-request form instead of showing only the merchant queue.</summary>
    [DataField]
    public bool RequestConsole;

    /// <summary>Currently selected station index (-1 = tree view).</summary>
    public int SelectedStation = -1;

    /// <summary>Currently selected goods category.</summary>
    public string? Category;

    /// <summary>Cart: station goods index -> count.</summary>
    public Dictionary<int, int> Cart = new();

    /// <summary>Saved carts (Eris saved_carts): named carts the merchant can reload.</summary>
    public List<OxydSavedCart> SavedCarts = new();

    /// <summary>Transaction log lines (Eris trade_log).</summary>
    public List<string> Log = new();
}

/// <summary>A named cart saved on a trade console (Eris saved_carts).</summary>
[DataDefinition]
public sealed partial class OxydSavedCart
{
    [DataField]
    public string Name = string.Empty;

    /// <summary>Station goods index -> count, captured when saved.</summary>
    [DataField]
    public Dictionary<int, int> Items = new();
}

/// <summary>Shared Asters Guild account (Eris department account): one entity spawned
/// lazily by OxydBankSystem; consoles and the ATM all transact against it.</summary>
[RegisterComponent]
public sealed partial class OxydBankAccountComponent : Component
{
    [DataField]
    public int Balance = 3000;
}

// ATM / trade beacons / cash register / EFTPOS components live in
// Content.Shared._Oxyd.Economy (Eris economy+PDA port) — do not re-register here.

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
