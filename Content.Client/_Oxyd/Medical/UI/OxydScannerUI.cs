using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Eris scanner readout UI port (hosted on a proxy entity).</summary>
public sealed class OxydScannerBoundUserInterface : BoundUserInterface
{
    private OxydScannerWindow? _window;

    public OxydScannerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        // The engine may queue Open() twice for proxy-spawned BUIs (state-apply +
        // interface startup); a second CreateWindow would assert in RegisterControl.
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydScannerWindow>();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydScannerState scan)
            _window?.UpdateState(scan);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _window?.Dispose();
            _window = null;
        }
        base.Dispose(disposing);
    }
}
