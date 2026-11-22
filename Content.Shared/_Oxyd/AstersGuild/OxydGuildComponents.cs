using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
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
    /// <summary>Department whose account the console uses until someone logs in another.</summary>
    [DataField]
    public string DefaultDepartment = "Guild";

    /// <summary>Account the console buys with and is paid into. Null = <see cref="DefaultDepartment"/>.</summary>
    [ViewVariables]
    public int? Account;

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

    /// <summary>Trade station the cart was filled at.</summary>
    [DataField]
    public string Station = string.Empty;

    /// <summary>Station goods index -> count, captured when saved.</summary>
    [DataField]
    public Dictionary<int, int> Items = new();
}

// ATM / trade beacons / cash register / EFTPOS components live in
// Content.Shared._Oxyd.Economy (Eris economy+PDA port) — do not re-register here.

/// <summary>Eris deep_drill: slowly extracts ore stacks from the tile beneath.</summary>
[RegisterComponent]
public sealed partial class OxydDeepDrillComponent : Component
{
    /// <summary>Ores the drill can bring up, picked at random each cycle.</summary>
    [DataField]
    public List<EntProtoId> Ores = new();

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
