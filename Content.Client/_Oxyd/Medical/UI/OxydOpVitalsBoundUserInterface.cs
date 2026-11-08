using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Eris operating computer (machinery/computer/Operating.dm) UI port.</summary>
public sealed class OxydOpVitalsBoundUserInterface : BoundUserInterface
{
    private OxydOpVitalsWindow? _window;

    public OxydOpVitalsBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        // The engine may queue Open() twice; a second CreateWindow asserts in RegisterControl.
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydOpVitalsWindow>();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydOperatingComputerState vitals)
            _window?.UpdateState(vitals);
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
