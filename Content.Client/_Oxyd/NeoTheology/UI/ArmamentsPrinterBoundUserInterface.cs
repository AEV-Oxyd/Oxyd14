using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.NeoTheology.UI;

/// <summary>
/// P2.16: the printer's window. It forwards the clicked armament id and nothing else — range,
/// follower status and cost are revalidated on the server.
/// </summary>
public sealed class ArmamentsPrinterBoundUserInterface : BoundUserInterface
{
    private ArmamentsPrinterWindow? _window;

    public ArmamentsPrinterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArmamentsPrinterWindow>();
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

        if (state is ArmamentsPrinterState printerState)
            _window?.UpdateState(printerState);
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
