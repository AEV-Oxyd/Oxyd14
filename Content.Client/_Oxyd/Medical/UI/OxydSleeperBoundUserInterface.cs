using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Eris sleeper pod UI port.</summary>
public sealed class OxydSleeperBoundUserInterface : BoundUserInterface
{
    private OxydSleeperWindow? _window;

    public OxydSleeperBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        // The engine may queue Open() twice for proxy-spawned BUIs (state-apply +
        // interface startup); a second CreateWindow would assert in RegisterControl.
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydSleeperWindow>();
        _window.InjectRequested += (id, dose) => SendMessage(new OxydSleeperInjectMessage { Reagent = id, Dose = dose });
        _window.EjectRequested += () => SendMessage(new OxydSleeperEjectMessage());
        _window.EjectBeakerRequested += () => SendMessage(new OxydSleeperEjectBeakerMessage());
        _window.ToggleDialysisRequested += () => SendMessage(new OxydSleeperToggleFilterMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydSleeperState sleeper)
            _window?.UpdateState(sleeper);
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
