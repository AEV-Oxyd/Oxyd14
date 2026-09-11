using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Oxyd.NeoTheology.Prototypes;

/// <summary>
/// Eris <c>/datum/nt_blueprint</c> (rituals/construction.dm): one structure the acolyte can
/// raise with Manifestation and take down with Uproot. The book choice lists every
/// prototype id sorted by ordinal string order, so the option list is deterministic.
/// </summary>
[Prototype("oxydNtBlueprint")]
public sealed partial class NeoTheologyBlueprintPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>Localized display name the caster picks in the litany book.</summary>
    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>The entity the ritual raises on the tile the caster faces.</summary>
    [DataField(required: true)]
    public EntProtoId Build { get; private set; } = string.Empty;

    /// <summary>
    /// Eris per-blueprint <c>build_time</c>. The cast transaction uses one shared delay
    /// (the litany's <c>extraDelay</c>), because the effects run in one synchronous apply
    /// step; this field records the source value for audits.
    /// </summary>
    [DataField]
    public TimeSpan BuildTime { get; private set; } = TimeSpan.FromSeconds(3);

    [DataField]
    public List<NeoTheologyMaterialRequirement> Materials { get; private set; } = new();
}

/// <summary>
/// One Eris materials entry. A requirement names either a material stack (units are
/// stack counts) or a whole single item, never both. Manifestation consumes it from
/// the front tile; Uproot returns it.
/// </summary>
[DataDefinition]
public sealed partial class NeoTheologyMaterialRequirement
{
    [DataField]
    public ProtoId<StackPrototype>? Stack { get; private set; }

    [DataField]
    public EntProtoId? Item { get; private set; }

    [DataField]
    public int Amount { get; private set; } = 1;
}
