using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Universal base BoundUserInterface for individual Program entities.
/// Delivers direct BUI states and messages without proxying through Computer or OS BUIs.
/// Attaches its <see cref="ProgramUiControl"/> cleanly via <see cref="ClientComputerUiSystem"/>.
/// </summary>
public abstract class ProgramBoundUserInterface : BoundUserInterface
{
    public ProgramUiControl? Control { get; protected set; }

    protected EntityUid ComputerUid;
    protected bool IsBootProgram;

    protected ProgramBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (Control == null)
            return;

        Control.UpdateProgramState(state);

        for (var cur = Control.Parent; cur != null; cur = cur.Parent)
        {
            if (cur is PipScreenSurface surface)
            {
                surface.InvalidateCapture();
                PipSurfaceRegistry.InvalidateDevice(surface.DeviceGuid);
                break;
            }
        }
    }
}

/// <summary>
/// Generic strongly-typed BoundUserInterface wrapper for every concrete program entity prototype.
/// </summary>
public abstract class ProgramBoundUserInterface<TControl> : ProgramBoundUserInterface
    where TControl : ProgramUiControl, new()
{
    private BoundUserInterfaceState? _pendingState;

    protected ProgramBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        var wasOpened = IsOpened;
        base.Open();

        if (wasOpened && Control != null && !Control.Disposed)
        {
            if (_pendingState != null)
            {
                Control.UpdateProgramState(_pendingState);
                _pendingState = null;
            }
            return;
        }

        if (EntMan.TryGetComponent<ProgramComponent>(Owner, out var progComp))
        {
            ComputerUid = progComp.Computer ?? EntityUid.Invalid;
            IsBootProgram = progComp.IsBootProgram;
        }

        if (Control != null)
        {
            if (ComputerUid.IsValid())
                EntMan.System<ClientComputerUiSystem>().UnregisterProgramUi(ComputerUid, Owner, Control);

            if (!Control.Disposed)
            {
                Control.Parent?.RemoveChild(Control);
                Control.Dispose();
            }
            Control = null;
        }

        Control = IoCManager.Resolve<IDynamicTypeFactory>().CreateInstance<TControl>();
        Control.ProgramUid = Owner;
        Control.ComputerUid = ComputerUid;
        Control.BindSendMessage(SendMessage);

        if (ComputerUid.IsValid())
            EntMan.System<ClientComputerUiSystem>().RegisterProgramUi(ComputerUid, Owner, IsBootProgram, Control);

        if (_pendingState != null)
        {
            Control.UpdateProgramState(_pendingState);
            _pendingState = null;
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        _pendingState = state;
        base.UpdateState(state);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || Control == null)
            return;

        if (ComputerUid.IsValid())
            EntMan.System<ClientComputerUiSystem>().UnregisterProgramUi(ComputerUid, Owner, Control);

        if (Control is IOsProgramUiHost osHost && !Control.Disposed)
            osHost.DetachAllChildPrograms();

        if (!Control.Disposed)
        {
            Control.Parent?.RemoveChild(Control);
            Control.Dispose();
        }
        Control = null;
    }
}
