using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>MIRC cartridge UI fragment — read-only reagent catalogue.</summary>
public sealed partial class OxydMircUi : UIFragment
{
    private OxydMircUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydMircUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydMircUiState mirc)
            _fragment?.UpdateState(mirc.Entries);
    }
}
