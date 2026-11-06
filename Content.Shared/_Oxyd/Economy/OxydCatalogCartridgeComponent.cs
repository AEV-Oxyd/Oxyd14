using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Economy;

/// <summary>
/// Read-only catalog cartridge (Eris drink/cook catalog programs). The client builds the
/// page from the catalog prototype; nothing is sent in UI state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydCatalogCartridgeComponent : Component
{
    /// <summary>Which catalog dataset to show.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<OxydCatalogPrototype> CatalogId = "drinks";
}

[DataDefinition]
public sealed partial class OxydCatalogEntry
{
    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;
}

[Prototype("oxydCatalog")]
public sealed partial class OxydCatalogPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Title = string.Empty;

    [DataField]
    public List<OxydCatalogEntry> Entries = new();
}
