using Content.Shared.CartridgeLoader;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

[RegisterComponent]
public sealed partial class OxydTradeOrdersCartridgeComponent : Component;

/// <summary>Eris sending trade beacon — export / fulfill offers from nearby items.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydTradeBeaconSendingComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 2.5f;

    [DataField, AutoNetworkedField]
    public int? LinkedAccountNumber;

    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [ViewVariables]
    public TimeSpan NextExportReady;
}

/// <summary>Eris receiving trade beacon — drop purchased order contents.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydTradeBeaconReceivingComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DropRadius = 2f;

    [DataField, AutoNetworkedField]
    public int? LinkedAccountNumber;
}

[Serializable, NetSerializable]
public enum OxydTradeUiTab : byte
{
    Stations,
    Goods,
    Cart,
    Orders,
    Offers,
    Logs,
}

[Serializable, NetSerializable]
public sealed class OxydTradeStationSummary
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Wealth;
    public int Favor;
    public int UniqueGoods;
    public int ActiveOffers;
}

[Serializable, NetSerializable]
public sealed class OxydTradeGoodView
{
    public string StationId = string.Empty;
    public string Product = string.Empty;
    public string Name = string.Empty;
    public string Category = string.Empty;
    public int Price;
    public int Stock;
}

[Serializable, NetSerializable]
public sealed class OxydTradeCartLine
{
    public string StationId = string.Empty;
    public string Product = string.Empty;
    public string Name = string.Empty;
    public int Price;
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class OxydTradeOrderView
{
    public string OrderId = string.Empty;
    public string Reason = string.Empty;
    public int Cost;
    public int Fee;
    public int RequestingAccount;
    public string RequestingName = string.Empty;
    public bool Fulfilled;
    public string ContentsSummary = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydTradeOfferView
{
    public string StationId = string.Empty;
    public string StationName = string.Empty;
    public string Product = string.Empty;
    public string Name = string.Empty;
    public int Price;
    public int AmountRemaining;
}

[Serializable, NetSerializable]
public sealed class OxydTradeLogView
{
    public string Id = string.Empty;
    public string Type = string.Empty;
    public string Account = string.Empty;
    public string Contents = string.Empty;
    public int Paid;
    public string Time = string.Empty;
}

[Serializable, NetSerializable]
public sealed class OxydTradeOrdersUiState : BoundUserInterfaceState
{
    public bool LoggedIn;
    public int AccountNumber;
    public string OwnerName = string.Empty;
    public int Balance;
    public bool IsGuild;
    public OxydTradeUiTab Tab = OxydTradeUiTab.Stations;
    public string? SelectedStationId;
    public string? Error;
    public string? Status;
    public List<OxydTradeStationSummary> Stations = new();
    public List<OxydTradeGoodView> Goods = new();
    public List<OxydTradeCartLine> Cart = new();
    public int CartTotal;
    public List<OxydTradeOrderView> Orders = new();
    public List<OxydTradeOfferView> Offers = new();
    public List<OxydTradeLogView> Logs = new();
}

[Serializable, NetSerializable]
public enum OxydTradeUiAction : byte
{
    Login,
    Logout,
    Refresh,
    SetTab,
    SelectStation,
    AddToCart,
    RemoveFromCart,
    ClearCart,
    PlaceOrder,
    CancelOrder,
    FulfillOrder,
    FulfillOffers,
}

[Serializable, NetSerializable]
public sealed class OxydTradeOrdersUiMessageEvent : CartridgeMessageEvent
{
    public OxydTradeUiAction Action;
    public int AccountNumber;
    public int Pin;
    public OxydTradeUiTab Tab;
    public string? StationId;
    public string? Product;
    public int Amount = 1;
    public string? OrderId;
    public string? Reason;
    public string? BeaconNetEntity; // unused; server finds nearest beacon

    public OxydTradeOrdersUiMessageEvent(OxydTradeUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum OxydTradeBeaconUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydTradeBeaconUiState : BoundUserInterfaceState
{
    public bool Sending;
    public int? LinkedAccount;
    public string Status = string.Empty;
    public string? Error;
    public bool Ready;
}

[Serializable, NetSerializable]
public sealed class OxydTradeBeaconLinkMessage : BoundUserInterfaceMessage
{
    public int AccountNumber;
}

[Serializable, NetSerializable]
public sealed class OxydTradeBeaconExportMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydTradeBeaconPurchaseMessage : BoundUserInterfaceMessage
{
    public string OrderId = string.Empty;
}
