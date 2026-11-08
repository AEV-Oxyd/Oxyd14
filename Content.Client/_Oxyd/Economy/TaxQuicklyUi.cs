using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Economy;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Economy;

public sealed partial class TaxQuicklyUi : UIFragment
{
    private TaxQuicklyUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new TaxQuicklyUiFragment();
        _fragment.OnAction += (action, account, pin, target, amount, text) =>
        {
            var ev = new TaxQuicklyUiMessageEvent(action)
            {
                AccountNumber = account,
                Pin = pin,
                TargetAccount = target,
                Amount = amount,
                Text = text,
            };
            userInterface.SendMessage(new CartridgeUiMessage(ev));
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is TaxQuicklyUiState tax)
            _fragment?.UpdateState(tax);
    }
}
