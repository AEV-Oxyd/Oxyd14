using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.AstersGuild;

/// <summary>
/// Shared BUI state/messages for the Eris Asters Guild windows.
/// Trade console recreates nano/templates/trade.tmpl (+ goods/offers/cart/orders/saved/log).
/// Ore processor console recreates the "Processor" TGUI layout in ErisNanoStyle.
/// </summary>

// ---------------- Trade console ----------------

[Serializable, NetSerializable]
public enum OxydTradeUiKey : byte
{
    Key,
}

/// <summary>One station entry on the trade tree (Eris stations list).</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeStationEntry
{
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Nick = string.Empty;
    /// <summary>Discovered (green) or undiscovered (red "?") in the Eris tree.</summary>
    public bool Discovered;
    public int Favor;
    public int FavorMax;
    /// <summary>Seconds left on the current special offer.</summary>
    public int OfferSecondsLeft;
}

/// <summary>One goods row (Eris trade_goods: name/price/available/cart).</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeGoodEntry
{
    /// <summary>Index into the station's goods list.</summary>
    public int Id;
    public string Name = string.Empty;
    public int Price;
    public int Available;
    public int InCart;
}

/// <summary>One special offer row (Eris trade_offers).</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeOfferEntry
{
    public int Id;
    public string Station = string.Empty;
    public string Name = string.Empty;
    public int Price;
    public int Amount;
    public int SecondsLeft;
}

/// <summary>One order request from a crew console (Eris trade_orders queue).</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeRequestEntry
{
    public int Id;
    public string Item = string.Empty;
    public int Amount;
    public string From = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydTradeState : BoundUserInterfaceState
{
    public int Balance;
    public bool HasAccount = true;
    public string ReceivingBeacon = string.Empty; // "✓Receiving Beacon: <loc>" or "No"
    public string SendingBeacon = string.Empty;
    public List<OxydTradeStationEntry> Stations = new();
    public int SelectedStation = -1;
    public string SelectedName = string.Empty;
    public string SelectedDesc = string.Empty;
    public List<string> Categories = new();
    public string? Category;
    public List<OxydTradeGoodEntry> Goods = new();
    public List<OxydTradeOfferEntry> Offers = new();
    public int CartCount;
    public int CartTotal;
    public List<string> CartLines = new();
    public List<OxydTradeRequestEntry> Requests = new();
    public List<string> SavedCartNames = new();
    /// <summary>Crew-side request console (Eris preset/trade_orders): show the submit form.</summary>
    public bool IsRequestConsole;
    public List<string> Log = new();
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydTradeSelectStationMessage : BoundUserInterfaceMessage
{
    public int Index;
}

[Serializable, NetSerializable]
public sealed class OxydTradeSelectCategoryMessage : BoundUserInterfaceMessage
{
    public string? Category;
}

[Serializable, NetSerializable]
public sealed class OxydTradeCartAdjustMessage : BoundUserInterfaceMessage
{
    /// <summary>Goods id in station list; delta -1/0/+1 (0 = clear).</summary>
    public int GoodId;
    public int Delta;
}

[Serializable, NetSerializable]
public sealed class OxydTradeCreateOrderMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydTradeResetCartMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydTradeSendOfferMessage : BoundUserInterfaceMessage
{
    public int OfferId;
}

/// <summary>Eris saved_carts: persist the current cart under a name.</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeSaveCartMessage : BoundUserInterfaceMessage
{
    public string Name = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydTradeLoadCartMessage : BoundUserInterfaceMessage
{
    public int Index;
}

[Serializable, NetSerializable]
public sealed class OxydTradeDeleteSavedCartMessage : BoundUserInterfaceMessage
{
    public int Index;
}

/// <summary>Eris trade_orders: crew submits a goods request to the merchant queue.</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeSubmitRequestMessage : BoundUserInterfaceMessage
{
    public string Item = string.Empty;
    public int Amount = 1;
}

/// <summary>Merchant clears a request off the queue (Eris order_requests list).</summary>
[Serializable, NetSerializable]
public sealed class OxydTradeCompleteRequestMessage : BoundUserInterfaceMessage
{
    public int Id;
}

// ---------------- ATM (Eris economy/ATM.dm) ----------------

[Serializable, NetSerializable]
public enum OxydAtmUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydAtmState : BoundUserInterfaceState
{
    public int Balance;
}

[Serializable, NetSerializable]
public sealed class OxydAtmWithdrawMessage : BoundUserInterfaceMessage
{
    public int Amount;
}

// ---------------- Ore processor console ----------------

[Serializable, NetSerializable]
public enum OxydOreProcessorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydOreProcessorState : BoundUserInterfaceState
{
    public bool HasMachine;
    public bool Running;
    /// <summary>Material rows: name, loaded count, storing toggle.</summary>
    public List<OxydOreProcessorMaterial> Materials = new();
    public List<string> Alloys = new();
}

[Serializable, NetSerializable]
public sealed class OxydOreProcessorMaterial
{
    public string Name = string.Empty;
    public int Loaded;
    public bool Storing;
}

[Serializable, NetSerializable]
public sealed class OxydOreProcessorToggleMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class OxydOreProcessorStoreMessage : BoundUserInterfaceMessage
{
    public string Material = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydOreProcessorAlloyMessage : BoundUserInterfaceMessage
{
    public string Alloy = string.Empty;
}
