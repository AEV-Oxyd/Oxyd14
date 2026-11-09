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
            _primaryCartridgeUI?.UpdateState(osStateFromPda);
        else if (pdaState?.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embStateFromPda)
            _primaryCartridgeUI?.UpdateState(embStateFromPda);

        // TODO move this to a component state and ensure the net ids.
        var programs = GetCartridgeComponents(_entManager.GetEntityList(loaderUiState.Programs));
        UpdateAvailablePrograms(programs);

        // Ensure OS is established as the primary shell if an OS program exists on this device
        if (_primaryUiFragment is null)
        {
            EntityUid? osProg = null;
            if (pdaState?.BootProgram is { } bootNet)
            {
                var bootEnt = _entManager.GetEntity(bootNet);
                if (bootEnt.IsValid() && (_entManager.HasComponent<Content.Shared._Oxyd.Computers.OsCartridgeComponent>(bootEnt) ||
                                          _entManager.HasComponent<Content.Shared._Oxyd.Computers.EmbeddedOsCartridgeComponent>(bootEnt)))
                    osProg = bootEnt;
            }

            if (osProg is null)
            {
                foreach (var netProg in loaderUiState.Programs)
                {
                    var p = _entManager.GetEntity(netProg);
                    if (p.IsValid() && (_entManager.HasComponent<Content.Shared._Oxyd.Computers.OsCartridgeComponent>(p) ||
                                        _entManager.HasComponent<Content.Shared._Oxyd.Computers.EmbeddedOsCartridgeComponent>(p)))
                    {
                        osProg = p;
                        break;
                    }
                }
            }

            if (osProg is not null)
            {
                var osUi = RetrieveCartridgeUI(osProg);
                var osComp = RetrieveCartridgeComponent(osProg);
                var osControl = osUi?.GetUIFragmentRoot();

                if (osControl is not null)
                {
                    _primaryCartridgeUI = osUi;
                    _primaryUiFragment = osControl;
                    _primaryProgram = osProg;

                    AttachCartridgeUI(osControl, Loc.GetString(osComp?.ProgramName ?? "default-program-name"));
                    SendCartridgeUiReadyEvent(osProg.Value);

                    if (osStateFromPda != null)
                    {
                        _primaryCartridgeUI?.UpdateState(osStateFromPda);
                    }
                }
            }
        }

        var activeUI = _entManager.GetEntity(loaderUiState.ActiveUI);

        if (activeUI == null)
        {
            if (_primaryProgram is not null)
            {
                activeUI = _primaryProgram;
            }
            else
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
        }

        // Sub-program running under the OS: maintain the OS as the primary shell
        if (_primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.OsUiFragment ||
            _primaryUiFragment is Content.Client._Oxyd.Computers.Cartridges.EmbeddedOsUiFragment)
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
                if (osStateFromPda != null)
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                else if (pdaState?.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embState)
                    _primaryCartridgeUI?.UpdateState(embState);
                else if (_lastCartridgeState is Content.Shared._Oxyd.Computers.OsUiState lastOsState)
                    _primaryCartridgeUI?.UpdateState(lastOsState);
                else if (_lastCartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState lastEmbState)
                    _primaryCartridgeUI?.UpdateState(lastEmbState);
                return;
            }

            if (activeUI == _subProgram && _subUiFragment is not null)
            {
                if (osStateFromPda != null)
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                else if (pdaState?.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embState)
                    _primaryCartridgeUI?.UpdateState(embState);

                if (pdaState?.CartridgeState != null && pdaState.CartridgeState is not Content.Shared._Oxyd.Computers.OsUiState && pdaState.CartridgeState is not Content.Shared._Oxyd.Computers.EmbeddedOsUiState)
                    _subCartridgeUI?.UpdateState(pdaState.CartridgeState);
                else if (_lastCartridgeState != null && _lastCartridgeState is not Content.Shared._Oxyd.Computers.OsUiState && _lastCartridgeState is not Content.Shared._Oxyd.Computers.EmbeddedOsUiState)
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
                    _primaryCartridgeUI?.UpdateState(osStateFromPda);
                else if (pdaState?.CartridgeState is Content.Shared._Oxyd.Computers.EmbeddedOsUiState embState)
                    _primaryCartridgeUI?.UpdateState(embState);

                if (pdaState?.CartridgeState != null && pdaState.CartridgeState is not Content.Shared._Oxyd.Computers.OsUiState && pdaState.CartridgeState is not Content.Shared._Oxyd.Computers.EmbeddedOsUiState)
                    _subCartridgeUI?.UpdateState(pdaState.CartridgeState);
                else if (_lastCartridgeState != null && _lastCartridgeState is not Content.Shared._Oxyd.Computers.OsUiState && _lastCartridgeState is not Content.Shared._Oxyd.Computers.EmbeddedOsUiState)
                    _subCartridgeUI?.UpdateState(_lastCartridgeState);
            }
            return;
        }

        if (activeUI == _activeProgram && _activeUiFragment is not null)
        {
            if (pdaState?.CartridgeState != null)
                _activeCartridgeUI?.UpdateState(pdaState.CartridgeState);
            else if (_lastCartridgeState != null)
                _activeCartridgeUI?.UpdateState(_lastCartridgeState);
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
            if (pdaState?.CartridgeState != null)
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
            _activeUiFragment = null;
            _primaryUiFragment = null;
            _subUiFragment = null;
            _activeCartridgeUI = null;
            _primaryCartridgeUI = null;
            _subCartridgeUI = null;
            _activeProgram = null;
            _primaryProgram = null;
            _subProgram = null;
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
