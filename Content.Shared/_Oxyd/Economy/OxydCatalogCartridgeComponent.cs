using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Read-only catalog cartridge (Eris drink/cook catalog programs).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydCatalogCartridgeComponent : Component
{
    /// <summary>Which catalog dataset to show.</summary>
    [DataField, AutoNetworkedField]
    public string CatalogId = "drinks";
}

[Serializable, NetSerializable]
public sealed class OxydCatalogUiState : BoundUserInterfaceState
{
    public string Title = string.Empty;
    public List<OxydCatalogEntry> Entries = new();
}

[DataDefinition, Serializable, NetSerializable]
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
