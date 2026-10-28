using Robust.Shared.Prototypes;
using Content.Shared._Oxyd.NeoTheology.Components;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Eris offering data: collect matching items near the Eye, add five power and restrict
/// the next miracle to the offering's reward family.
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

    [DataField] public float Power = 5f;
    [DataField] public List<NeoTheologyMiracle> Rewards = new();
}

[DataDefinition]
public sealed partial class OfferingRequirement
{
    /// <summary>Prototype the offered item must match (ancestors included).</summary>
    [DataField]
    public EntProtoId? Proto;

    /// <summary>Any native oddity, not only the Eye's honorary seal.</summary>
    [DataField] public bool Oddity;

    /// <summary>Total count of matching items (stacks sum their <c>Stack.Count</c>).</summary>
    [DataField]
    public int Count = 1;
}
