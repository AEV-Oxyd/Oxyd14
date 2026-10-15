using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Eris /datum/trade_station inventory line.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class OxydTradeGoodEntry
{
    [DataField(required: true)]
    public EntProtoId Product = string.Empty;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Category = "General";

    [DataField]
    public int Price = 100;

    /// <summary>Starting stock (restocks over time via wealth).</summary>
    [DataField]
    public int Stock = 12;
}

/// <summary>Eris special offer — sell N matching prototypes for a payout.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class OxydTradeOfferEntry
{
    [DataField(required: true)]
    public EntProtoId Product = string.Empty;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public int Price = 200;

    [DataField]
    public int Amount = 3;
}

/// <summary>Discovered trade beacon / station catalog (Eris trade_station).</summary>
[Prototype("oxydTradeStation")]
public sealed partial class OxydTradeStationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public bool StartDiscovered = true;

    [DataField]
    public float Markup = 1.2f;

    [DataField]
    public int BaseIncome = 1600;

    [DataField]
    public List<OxydTradeGoodEntry> Inventory = new();

    [DataField]
    public List<OxydTradeOfferEntry> Offers = new();
}
