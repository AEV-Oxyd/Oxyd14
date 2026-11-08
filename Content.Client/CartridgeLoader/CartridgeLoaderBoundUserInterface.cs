using Content.Client.UserInterface.Fragments;
using Content.Client.UserInterface.StateRouting;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Shared.Serialization.Manager;

namespace Content.Client.CartridgeLoader;


public abstract class CartridgeLoaderBoundUserInterface : RoutedBoundUserInterface
{
    [ViewVariables]
    protected EntityUid? _activeProgram;

    [ViewVariables]
    private UIFragment? _activeCartridgeUI;

    [ViewVariables]
    private Control? _activeUiFragment;

    [ViewVariables]
    private UIFragment? _primaryCartridgeUI;
    [ViewVariables]
    private Control? _primaryUiFragment;
    [ViewVariables]
    private EntityUid? _primaryProgram;

    [ViewVariables]
    private UIFragment? _subCartridgeUI;
    [ViewVariables]
    private Control? _subUiFragment;
    [ViewVariables]
    private EntityUid? _subProgram;

    [ViewVariables]
    private BoundUserInterfaceState? _lastCartridgeState;

    private IEntityManager _entManager;

    protected CartridgeLoaderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _entManager = IoCManager.Resolve<IEntityManager>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CartridgeLoaderUiState loaderUiState)
        {
            _lastCartridgeState = state;
            if (state is Content.Shared._Oxyd.Computers.OsUiState)
                _primaryCartridgeUI?.UpdateState(state);
            else if (_subCartridgeUI != null)
                _subCartridgeUI.UpdateState(state);
            else
                _activeCartridgeUI?.UpdateState(state);
            return;
        }

        var pdaState = state as Content.Shared.PDA.PdaUpdateState;
        var osStateFromPda = pdaState?.OsState ?? (pdaState?.CartridgeState as Content.Shared._Oxyd.Computers.OsUiState);
        if (osStateFromPda != null)
        {
            _primaryCartridgeUI?.UpdateState(osStateFromPda);
            if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                primaryFrag.UpdateState(osStateFromPda);
        }

        // TODO move this to a component state and ensure the net ids.
        var programs = GetCartridgeComponents(_entManager.GetEntityList(loaderUiState.Programs));
        UpdateAvailablePrograms(programs);

        var activeUI = _entManager.GetEntity(loaderUiState.ActiveUI);

        if (activeUI == null)
        {
            if (_subUiFragment is not null)
            {
                DetachCartridgeUI(_subUiFragment);
                _subUiFragment.Dispose();
                _subUiFragment = null;
                _subCartridgeUI = null;
                _subProgram = null;
            }
            if (_activeUiFragment is not null)
            {
                DetachCartridgeUI(_activeUiFragment);
                _activeUiFragment.Dispose();
                _activeUiFragment = null;
                _activeCartridgeUI = null;
                _activeProgram = null;
            }
            return;
        }

        // Sub-program running under the OS: maintain the OS as the primary shell
        if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment)
        {
            if (activeUI == _primaryProgram)
            {
                // Returned to the OS
                if (_subUiFragment is not null)
                {
                    DetachCartridgeUI(_subUiFragment);
                    _subUiFragment.Dispose();
                    _subUiFragment = null;
                    _subCartridgeUI = null;
                    _subProgram = null;
                }
                _activeProgram = activeUI;
                _activeCartridgeUI = _primaryCartridgeUI;
                _activeUiFragment = _primaryUiFragment;
                var primaryComp = RetrieveCartridgeComponent(activeUI.Value);
                AttachCartridgeUI(_primaryUiFragment, Loc.GetString(primaryComp?.ProgramName ?? "default-program-name"));
                if (osStateFromPda != null)
                {
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                    if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                        primaryFrag.UpdateState(osStateFromPda);
                }
                else if (_lastCartridgeState is Content.Shared._Oxyd.Computers.OsUiState lastOsState)
                {
                    _primaryCartridgeUI?.UpdateState(lastOsState);
                    if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                        primaryFrag.UpdateState(lastOsState);
                }
                return;
            }

            if (activeUI == _subProgram && _subUiFragment is not null)
            {
                var activeComp = RetrieveCartridgeComponent(activeUI.Value);
                AttachCartridgeUI(_subUiFragment, Loc.GetString(activeComp?.ProgramName ?? "default-program-name"));
                if (osStateFromPda != null)
                {
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                    if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                        primaryFrag.UpdateState(osStateFromPda);
                }
                if (pdaState?.CartridgeState != null)
                    _subCartridgeUI?.UpdateState(pdaState.CartridgeState);
                else if (_lastCartridgeState != null)
                    _subCartridgeUI?.UpdateState(_lastCartridgeState);
                return;
            }

            // New subprogram under the OS
            if (_subUiFragment is not null)
            {
                DetachCartridgeUI(_subUiFragment);
                _subUiFragment.Dispose();
                _subUiFragment = null;
                _subCartridgeUI = null;
                _subProgram = null;
            }

            var subUi = RetrieveCartridgeUI(activeUI);
            var subComp = RetrieveCartridgeComponent(activeUI);
            var subControl = subUi?.GetUIFragmentRoot();

            if (subControl is not null)
            {
                _subCartridgeUI = subUi;
                _subUiFragment = subControl;
                _subProgram = activeUI;
                _activeProgram = activeUI;
                _activeCartridgeUI = subUi;
                _activeUiFragment = subControl;

                AttachCartridgeUI(subControl, Loc.GetString(subComp?.ProgramName ?? "default-program-name"));
                SendCartridgeUiReadyEvent(activeUI.Value);

                if (osStateFromPda != null)
                {
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                    if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                        primaryFrag.UpdateState(osStateFromPda);
                }
                if (pdaState?.CartridgeState != null)
                    _subCartridgeUI?.UpdateState(pdaState.CartridgeState);
                else if (_lastCartridgeState != null)
                    _subCartridgeUI?.UpdateState(_lastCartridgeState);
            }
            return;
        }

        if (activeUI == _activeProgram && _activeUiFragment is not null)
        {
            var existingComp = RetrieveCartridgeComponent(activeUI.Value);
            AttachCartridgeUI(_activeUiFragment, Loc.GetString(existingComp?.ProgramName ?? "default-program-name"));
            if (pdaState?.CartridgeState != null && _activeUiFragment is not IBuiStateReceiver)
            {
                _activeCartridgeUI?.UpdateState(pdaState.CartridgeState);
            }
            else if (_lastCartridgeState != null && _activeUiFragment is not IBuiStateReceiver)
            {
                _activeCartridgeUI?.UpdateState(_lastCartridgeState);
            }
            return;
        }

        var prevFragment = _activeUiFragment;
        _activeProgram = activeUI;

        if (prevFragment is not null)
        {
            DetachCartridgeUI(prevFragment);
            prevFragment.Dispose();
            _activeUiFragment = null;
            _activeCartridgeUI = null;
        }

        var ui = RetrieveCartridgeUI(activeUI);
        var comp = RetrieveCartridgeComponent(activeUI);
        var control = ui?.GetUIFragmentRoot();

        if (control is not null)
        {
            AttachCartridgeUI(control, Loc.GetString(comp?.ProgramName ?? "default-program-name"));
            SendCartridgeUiReadyEvent(activeUI.Value);

            _activeCartridgeUI = ui;
            _activeUiFragment = control;
            if (_primaryUiFragment == null)
            {
                _primaryCartridgeUI = ui;
                _primaryUiFragment = control;
                _primaryProgram = activeUI;
            }

            if (osStateFromPda != null)
            {
                _primaryCartridgeUI?.UpdateState(osStateFromPda);
                if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment primaryFrag)
                    primaryFrag.UpdateState(osStateFromPda);
            }
            else if (pdaState?.CartridgeState != null)
            {
                _activeCartridgeUI?.UpdateState(pdaState.CartridgeState);
            }
            else if (_lastCartridgeState != null)
            {
                _activeCartridgeUI?.UpdateState(_lastCartridgeState);
            }
        }
    }

    protected void ActivateCartridge(EntityUid cartridgeUid)
    {
        var message = new CartridgeLoaderUiMessage(_entManager.GetNetEntity(cartridgeUid), CartridgeUiMessageAction.Activate);
        SendPredictedMessage(message);
    }

    protected void DeactivateActiveCartridge()
    {
        if (!_activeProgram.HasValue)
            return;

        var message = new CartridgeLoaderUiMessage(_entManager.GetNetEntity(_activeProgram.Value), CartridgeUiMessageAction.Deactivate);
        SendPredictedMessage(message);
    }

    protected void InstallCartridge(EntityUid cartridgeUid)
    {
        var message = new CartridgeLoaderUiMessage(_entManager.GetNetEntity(cartridgeUid), CartridgeUiMessageAction.Install);
        SendPredictedMessage(message);
    }

    protected void UninstallCartridge(EntityUid cartridgeUid)
    {
        var message = new CartridgeLoaderUiMessage(_entManager.GetNetEntity(cartridgeUid), CartridgeUiMessageAction.Uninstall);
        SendPredictedMessage(message);
    }

    private List<(EntityUid, CartridgeComponent)> GetCartridgeComponents(List<EntityUid> programs)
    {
        var components = new List<(EntityUid, CartridgeComponent)>();

        foreach (var program in programs)
        {
            var component = RetrieveCartridgeComponent(program);
            if (component is not null)
                components.Add((program, component));
        }

        return components;
    }

    /// <summary>
    /// The implementing ui needs to add the passed ui fragment as a child to itself
    /// </summary>
    protected abstract void AttachCartridgeUI(Control cartridgeUIFragment, string? title);

    /// <summary>
    /// The implementing ui needs to remove the passed ui from itself
    /// </summary>
    protected abstract void DetachCartridgeUI(Control cartridgeUIFragment);

    protected abstract void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _activeUiFragment?.Dispose();
            _primaryUiFragment?.Dispose();
            _subUiFragment?.Dispose();
        }
    }

    protected CartridgeComponent? RetrieveCartridgeComponent(EntityUid? cartridgeUid)
    {
        return EntMan.GetComponentOrNull<CartridgeComponent>(cartridgeUid);
    }

    private void SendCartridgeUiReadyEvent(EntityUid cartridgeUid)
    {
        var message = new CartridgeLoaderUiMessage(_entManager.GetNetEntity(cartridgeUid), CartridgeUiMessageAction.UIReady);
        SendMessage(message);
    }

    private UIFragment? RetrieveCartridgeUI(EntityUid? cartridgeUid)
    {
        var component = EntMan.GetComponentOrNull<UIFragmentComponent>(cartridgeUid);
        if (component?.Ui == null)
            return null;

        var ui = IoCManager.Resolve<ISerializationManager>().CreateCopy(component.Ui, notNullableOverride: true);
        ui.Setup(this, cartridgeUid);
        return ui;
    }
}
