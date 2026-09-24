using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Eris <c>/datum/ritual/offering</c> ported as data (P3.8). The priest names an offering; the
/// altar collects matching items on its turf and the Eye converts them to observation.
/// Only <c>divine_intervention</c> is ported — the oddity/fruit offering has no fork equivalent.
/// </summary>
[Prototype("oxydOffering")]
public sealed partial class OfferingPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>Items that must be present on the altar, in total, for the offering to fire.</summary>
    [DataField]
    public List<OfferingRequirement> Required { get; private set; } = new();

    /// <summary>Observation credited to the Eye when the offering is made.</summary>
    [DataField]
    public float Observation;
}

[DataDefinition]
public sealed partial class OfferingRequirement
{
    /// <summary>Prototype the offered item must match (ancestors included).</summary>
    [DataField(required: true)]
    public EntProtoId Proto;

    /// <summary>Total count of matching items (stacks sum their <c>Stack.Count</c>).</summary>
    [DataField]
    public int Count = 1;
}
