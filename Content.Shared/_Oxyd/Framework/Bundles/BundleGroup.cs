using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype()]
public sealed partial class BundleGroup : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public int volume = 0;
}
