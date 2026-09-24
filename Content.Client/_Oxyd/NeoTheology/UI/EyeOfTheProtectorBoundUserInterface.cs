using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.NeoTheology.UI;

/// <summary>
/// P3.7: the Eye's window is read-only — it renders whatever state the server pushed and forwards
/// nothing back.
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

        _window = this.CreateWindow<EyeOfTheProtectorWindow>();
        _window.OnClose += Close;
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
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }
}
