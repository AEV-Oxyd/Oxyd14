using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Eris organ bioprinter UI (machinery/bioprinter.dm attack_hand list,
/// lifted to a NanoUI-style window like the other ported machines).</summary>
public sealed class OxydBioprinterBoundUserInterface : BoundUserInterface
{
    private OxydBioprinterWindow? _window;

    public OxydBioprinterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydBioprinterWindow>();
        _window.PrintRequested += id =>
            SendMessage(new OxydBioprinterPrintMessage { Product = id });
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydBioprinterState printer)
            _window?.UpdateState(printer);
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
