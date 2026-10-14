using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Economy;

public sealed partial class OxydCatalogUi : UIFragment
{
    private OxydCatalogUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydCatalogUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydCatalogUiState catalog)
            _fragment?.UpdateState(catalog);
    }
}
