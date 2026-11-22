using Content.Shared._Oxyd.Medical;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>Surgery UI hosted on the patient (Eris surgery_organ nano UI port).</summary>
public sealed class OxydSurgeryBoundUserInterface : BoundUserInterface
{
    private OxydSurgeryWindow? _window;

    public OxydSurgeryBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydSurgeryWindow>();
        _window.StepSelected += OnStepSelected;
        _window.OnClose += Close;
    }

    private void OnStepSelected(NetEntity organ, OxydSurgeryStep step)
    {
        SendMessage(new OxydSurgerySelectStepMessage(organ, step));
    }

    // The BUI is shared between surgeons on the patient, so the server refreshes
    // each open window with a per-actor message instead of a broadcast state.
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is OxydSurgeryStateMessage surgery)
            _window?.UpdateState(surgery.State);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydSurgeryState surgery)
            _window?.UpdateState(surgery);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window is not null)
        {
            _window.StepSelected -= OnStepSelected;
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
        }
        base.Dispose(disposing);
    }
}
