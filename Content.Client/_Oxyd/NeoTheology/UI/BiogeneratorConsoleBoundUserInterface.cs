using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.NeoTheology.UI;

/// <summary>Read-only: the biogenerator screen renders whatever snapshot the server pushes.</summary>
public sealed class BiogeneratorConsoleBoundUserInterface : BoundUserInterface
{
    private BiogeneratorConsoleWindow? _window;

    public BiogeneratorConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        // Idempotent: a queued double-open would re-register this BUI's control and assert.
        if (_window != null)
            return;

        _window = this.CreateWindow<BiogeneratorConsoleWindow>();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is BiogeneratorConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window is not null)
        {
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
