using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.Economy;

/// <summary>Catalog program: the page comes from the catalog prototype the cartridge names.</summary>
public sealed partial class OxydCatalogUi : UIFragment
{
    private OxydCatalogUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydCatalogUiFragment();

        var entMan = IoCManager.Resolve<IEntityManager>();
        var protos = IoCManager.Resolve<IPrototypeManager>();
        if (entMan.TryGetComponent(fragmentOwner, out OxydCatalogCartridgeComponent? cartridge) &&
            protos.TryIndex(cartridge.CatalogId, out var catalog))
            _fragment.Populate(catalog);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}
