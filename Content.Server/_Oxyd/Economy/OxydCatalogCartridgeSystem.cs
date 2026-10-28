using Content.Shared._Oxyd.Economy;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

public sealed partial class OxydCatalogCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydCatalogCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
    }

    private void OnReady(EntityUid uid, OxydCatalogCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        var state = new OxydCatalogUiState();
        if (_protos.TryIndex<OxydCatalogPrototype>(component.CatalogId, out var catalog))
        {
            state.Title = catalog.Title;
            state.Entries = catalog.Entries;
        }
        else
        {
            state.Title = component.CatalogId;
        }

        _cartridges.UpdateCartridgeUiState(args.Loader, state);
    }
}
