using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Eris <c>/datum/armament</c> ported as data (P2.15). Purchase state (how many times it has been
/// bought, and therefore its current discount) belongs to the printer that sold it, not here — see
/// <c>ArmamentsPrinterComponent.PurchaseCount</c>.
/// </summary>
[Prototype("oxydArmament")]
public sealed partial class ArmamentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    [DataField]
    public LocId? Desc { get; private set; }

    [DataField(required: true)]
    public EntProtoId Path { get; private set; }

    [DataField]
    public int Cost { get; private set; } = 100;

    [DataField]
    public int MinCost { get; private set; } = 10;

    /// <summary>Discount added per prior purchase of this armament.</summary>
    [DataField]
    public int DiscountIncrease { get; private set; } = 25;

    /// <summary>Null means the discount is never capped.</summary>
    [DataField]
    public int? MaxDiscount { get; private set; }

    /// <summary>Eris <c>max_points_increase</c>: printer point ceiling raised by the first purchase.</summary>
    [DataField]
    public int MaxPointsIncrease { get; private set; } = 25;

    /// <summary>Eris <c>get_cost()</c>: the floor wins over the discount.</summary>
    public int GetCost(int discount) => Math.Max(MinCost, Cost - discount);

    /// <summary>
    /// The discount a printer that has sold this armament <paramref name="purchaseCount"/> times must
    /// apply. Null <see cref="MaxDiscount"/> means uncapped.
    /// </summary>
    public int GetDiscount(int purchaseCount)
    {
        var discount = purchaseCount * DiscountIncrease;
        return MaxDiscount is { } max ? Math.Min(discount, max) : discount;
    }
}
