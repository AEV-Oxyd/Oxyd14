using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Eris autodoc UI port.</summary>
public sealed class OxydAutodocBoundUserInterface : BoundUserInterface
{
    private OxydAutodocWindow? _window;

    public OxydAutodocBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        // The engine may queue Open() twice for proxy-spawned BUIs (state-apply +
        // interface startup); a second CreateWindow would assert in RegisterControl.
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydAutodocWindow>();
        _window.ScanRequested += () => SendMessage(new OxydAutodocScanMessage());
        _window.ProcessAllRequested += () => SendMessage(new OxydAutodocProcessAllMessage());
        _window.ProcessPickedRequested += () => SendMessage(new OxydAutodocProcessPickedMessage());
        _window.AbortRequested += () => SendMessage(new OxydAutodocAbortMessage());
        _window.ToggleRequested += (id, op) => SendMessage(new OxydAutodocToggleMessage { EntryId = id, Op = op });
        _window.EjectRequested += () => SendMessage(new OxydAutodocEjectMessage());
        _window.EjectCreditsRequested += () => SendMessage(new OxydAutodocEjectCreditsMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydAutodocState doc)
            _window?.UpdateState(doc);
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
