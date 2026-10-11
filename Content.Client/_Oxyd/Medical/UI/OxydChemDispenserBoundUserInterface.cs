using Content.Shared._Oxyd.Medical;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Oxyd.Medical.UI;

/// <summary>
/// Eris Chem Dispenser 5000 BUI for the Oxyd dispenser: consumes the stock
/// ReagentDispenserBoundUserInterfaceState and sends the stock messages — the
/// only change is the NanoUI look. Bound in the OxydChemDispenser proto's
/// UserInterface map so stock dispensers keep their stock window.
/// </summary>
public sealed class OxydChemDispenserBoundUserInterface : BoundUserInterface
{
    private OxydReagentDispenserWindow? _window;

    public OxydChemDispenserBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_window != null)
            return;
        _window = this.CreateWindow<OxydReagentDispenserWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnDispenseAmountPressed += amount =>
            SendMessage(new ReagentDispenserSetDispenseAmountMessage($"{(int) amount}"));
        _window.OnDispenseReagentPressed += location =>
            SendMessage(new ReagentDispenserDispenseReagentMessage(location));
        _window.OnEjectBeakerPressed += () =>
            SendMessage(new ItemSlotButtonPressedEvent(SharedReagentDispenser.OutputSlotName));
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ReagentDispenserBoundUserInterfaceState dispenser)
            _window?.UpdateState(dispenser);
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
