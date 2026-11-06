using Content.Shared._Oxyd.Medical;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Cryogenics;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>
/// Eris cryo cell BUI for the Oxyd pod: consumes the stock CryoPodUserMessage stream
/// and sends the stock eject/inject messages plus OxydCryoPodPowerMessage for the
/// Eris On/Off link pair. Bound in the OxydCryoPod proto's UserInterface map so the
/// stock pod keeps its stock window.
/// </summary>
public sealed class OxydCryoCellBoundUserInterface : BoundUserInterface
{
    private OxydCryoPodWindow? _window;

    public OxydCryoCellBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydCryoPodWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnEjectPatientPressed += () =>
            SendMessage(new CryoPodSimpleUiMessage(CryoPodSimpleUiMessage.MessageType.EjectPatient));
        _window.OnEjectBeakerPressed += () =>
            SendMessage(new CryoPodSimpleUiMessage(CryoPodSimpleUiMessage.MessageType.EjectBeaker));
        _window.OnInjectPressed += amount => SendMessage(new CryoPodInjectUiMessage(amount));
        _window.OnPowerPressed += on => SendMessage(new OxydCryoPodPowerMessage { On = on });
        _window.OnClose += Close;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is CryoPodUserMessage cryoMsg)
            _window?.Populate(cryoMsg, Owner, EntMan);
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
