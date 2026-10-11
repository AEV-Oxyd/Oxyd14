using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.NeoTheology.UI;

/// <summary>
/// P3.7: the Eye's window renders the pushed status snapshot plus the Eris <c>eopt.tmpl</c>
/// armory list; a purchase forwards only the clicked armament id (the server revalidates it).
/// </summary>
public sealed class EyeOfTheProtectorBoundUserInterface : BoundUserInterface
{
    private EyeOfTheProtectorWindow? _window;

    public EyeOfTheProtectorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        // Idempotent: a queued double-open would re-register this BUI's control and assert.
        if (_window != null)
            return;

        _window = this.CreateWindow<EyeOfTheProtectorWindow>();
        _window.Purchase += OnPurchase;
        _window.OnClose += Close;
    }

    private void OnPurchase(string armamentId)
    {
        SendMessage(new PurchaseArmamentMessage(armamentId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is EyeOfTheProtectorState eyeState)
            _window?.UpdateState(eyeState);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window is not null)
        {
            _window.Purchase -= OnPurchase;
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
