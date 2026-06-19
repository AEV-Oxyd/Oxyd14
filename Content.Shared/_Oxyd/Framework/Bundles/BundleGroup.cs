using Content.Shared.Item;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("BundleGroup")]
public sealed partial class BundleGroup : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public int volume = 0;

    [DataField]
    // size used for bundle in inventory
    public ProtoId<ItemSizePrototype> size = "normal";

    [DataField("components")]
    public ComponentRegistry components =  new ComponentRegistry();
}
