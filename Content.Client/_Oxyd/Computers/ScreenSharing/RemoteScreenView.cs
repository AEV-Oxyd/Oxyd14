using Content.Client.PDA;
using Content.Client.Power.APC.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Client.UserInterface.StateRouting;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.APC;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Universal remote screen container capable of embedding:
/// - PDAs and CartridgeLoader devices (including nested terminals)
/// - Any BoundUserInterface device (APCs, Atmospheric Alarms, Static Computers, etc.)
/// - Picture-in-Picture (PiP) Hall-of-Mirrors viewports (<see cref="PipMirrorViewport"/>) whenever
///   a screen-sharing cycle (<c>A -> B -> A</c> or <c>A -> A</c>) is detected.
/// </summary>
public sealed partial class RemoteScreenView : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private ISerializationManager _serManager = default!;

    public BoundUserInterface? HostBui { get; set; }
    public event Action<string?, string?>? OnRemoteAction;

    private readonly PipScreenSurface _surface;
    private readonly BuiStateBarrier _barrier;

    private PipMirrorViewport? _mirrorViewport;
    private PdaMenu? _remotePdaMenu;
    private UIFragment? _remoteUi;
    private Control? _remoteControl;
    private EntityUid? _remoteProgram;
    private RemoteBuiScreenHost? _remoteBuiHost;

    public RemoteScreenView()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        _surface = new PipScreenSurface
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };

        _barrier = new BuiStateBarrier
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };

        _surface.AddChild(_barrier);
        AddChild(_surface);
    }

    public void UpdateState(RemoteScreenState state)
    {
        // Detect if this remote device is already an ancestor in the UI hierarchy (or flagged as a cycle by the server).
        // If so, render a Picture-in-Picture mirror viewport sampling the ancestor's offscreen texture instead of
        // recursively parenting a duplicate widget tree.
        var isCycle = state.IsCycleReference ||
                      PipSurfaceRegistry.IsDeviceInAncestorChain(this, state.DeviceGuid);

        if (isCycle)
        {
            DisposePdaContent();
            DisposeBuiContent();
            _surface.Visible = false;
            _surface.DeviceGuid = null;

            if (_mirrorViewport == null)
            {
                _mirrorViewport = new PipMirrorViewport();
                AddChild(_mirrorViewport);
            }

            _mirrorViewport.SetTarget(state.DeviceGuid, state.DeviceName);
            return;
        }

        if (_mirrorViewport != null)
        {
            _mirrorViewport.Orphan();
            _mirrorViewport.Dispose();
            _mirrorViewport = null;
        }

        _surface.Visible = true;
        _surface.DeviceGuid = state.DeviceGuid;

        // Branch 1: PDA / Cartridge Host Screen
        if (state.PdaState != null)
        {
            DisposeBuiContent();
            EnsurePdaMenu();
            _remotePdaMenu!.UpdateState(state.PdaState);
            UpdateRemoteCartridgeProgram(state.PdaState);
            return;
        }

        // Branch 2: Generic BoundUserInterface Device (APC, Air Alarm, Static Computer, etc.)
        if (state.UiKey != null && !string.IsNullOrEmpty(state.BuiClientType))
        {
            DisposePdaContent();
            EnsureBuiHost(state);
            _remoteBuiHost!.UpdateFromState(state);
            return;
        }
    }

    private void EnsurePdaMenu()
    {
        if (_remotePdaMenu != null)
            return;

        _remotePdaMenu = new PdaMenu();
        _remotePdaMenu.StripDetailElements(true);
        _remotePdaMenu.FlashLightToggleButton.Visible = false;
        _remotePdaMenu.EjectPenButton.Visible = false;
        _remotePdaMenu.EjectIdButton.Visible = false;
        _remotePdaMenu.EjectPaiButton.Visible = false;
        _remotePdaMenu.EjectDiskButton.Visible = false;
        _remotePdaMenu.PowerButton.Visible = false;

        _remotePdaMenu.OnTabSelected += progUid =>
        {
            var netUid = _entManager.GetNetEntity(progUid);
            OnRemoteAction?.Invoke("tab", netUid.ToString());
        };
        _remotePdaMenu.ProgramCloseButton.OnPressed += _ => OnRemoteAction?.Invoke("home", null);
        _remotePdaMenu.OnTabClosed += progUid =>
        {
            var netUid = _entManager.GetNetEntity(progUid);
            OnRemoteAction?.Invoke("close_tab", netUid.ToString());
        };
        _remotePdaMenu.HomeButton.OnPressed += _ => OnRemoteAction?.Invoke("home", null);
        _remotePdaMenu.ProgramListButton.OnPressed += _ => OnRemoteAction?.Invoke("programs", null);
        _remotePdaMenu.SettingsButton.OnPressed += _ => OnRemoteAction?.Invoke("settings", null);
        _remotePdaMenu.OnProgramItemPressed += progUid =>
        {
            var netUid = _entManager.GetNetEntity(progUid);
            OnRemoteAction?.Invoke("run_program", netUid.ToString());
        };

        _barrier.RemoveAllChildren();
        _barrier.AddChild(_remotePdaMenu);
    }

    private void UpdateRemoteCartridgeProgram(PdaUpdateState remote)
    {
        if (_remotePdaMenu == null)
            return;

        var programs = new List<(EntityUid, CartridgeComponent)>();
        foreach (var net in remote.Programs)
        {
            if (_entManager.TryGetEntity(net, out var prog) &&
                _entManager.TryGetComponent(prog, out CartridgeComponent? comp))
            {
                programs.Add((prog.Value, comp));
            }
        }
        _remotePdaMenu.UpdateAvailablePrograms(programs);

        EntityUid? active = null;
        if (remote.ActiveUI is { } activeNet && _entManager.TryGetEntity(activeNet, out var activeUid))
            active = activeUid;

        if (active == null)
        {
            if (_remoteControl != null)
            {
                DisposeRemoteProgram();
                _remotePdaMenu.ProgramView.RemoveAllChildren();
                _remotePdaMenu.HideProgramHeader();
                if (_remotePdaMenu.CurrentView == PdaMenu.ProgramContentView)
                    _remotePdaMenu.ToProgramList();
            }
            return;
        }

        if (_remoteProgram != active)
        {
            DisposeRemoteProgram();
            _remotePdaMenu.ProgramView.RemoveAllChildren();

            var fragmentComp = _entManager.GetComponentOrNull<UIFragmentComponent>(active.Value);
            var templateUi = fragmentComp?.Ui;
            if (templateUi != null && HostBui != null)
            {
                var ui = _serManager.CreateCopy(templateUi, notNullableOverride: true);
                ui.Setup(HostBui, active.Value);
                var control = ui.GetUIFragmentRoot();
                _remoteUi = ui;
                _remoteControl = control;
                _remoteProgram = active;
                _remotePdaMenu.ProgramView.AddChild(control);

                var title = _entManager.TryGetComponent(active.Value, out CartridgeComponent? cart)
                    ? Loc.GetString(cart.ProgramName)
                    : string.Empty;
                if (remote.CurrentView == PdaMenu.ProgramContentView)
                    _remotePdaMenu.ToProgramView(title);
            }
        }

        // Pass the remote cartridge state through unflattened so nested terminals can render
        // either a 2nd-hop remote device or a PiP Hall-of-Mirrors cascade!
        if (_remoteUi != null && remote.CartridgeState != null)
        {
            _remoteUi.UpdateState(remote.CartridgeState);
        }
    }

    private void EnsureBuiHost(RemoteScreenState state)
    {
        if (_remoteBuiHost != null &&
            _remoteBuiHost.TargetNetEntity == state.TargetEntity &&
            Equals(_remoteBuiHost.UiKey, state.UiKey))
        {
            return;
        }

        DisposeBuiContent();
        _remoteBuiHost = new RemoteBuiScreenHost(state.TargetEntity, state.UiKey!, state.DeviceName);
        _barrier.RemoveAllChildren();
        _barrier.AddChild(_remoteBuiHost);
    }

    private void DisposeRemoteProgram()
    {
        _remoteControl?.Orphan();
        _remoteControl?.Dispose();
        _remoteControl = null;
        _remoteUi = null;
        _remoteProgram = null;
    }

    private void DisposePdaContent()
    {
        DisposeRemoteProgram();
        if (_remotePdaMenu != null)
        {
            _remotePdaMenu.Orphan();
            _remotePdaMenu.Dispose();
            _remotePdaMenu = null;
        }
    }

    private void DisposeBuiContent()
    {
        if (_remoteBuiHost != null)
        {
            _remoteBuiHost.Orphan();
            _remoteBuiHost.Dispose();
            _remoteBuiHost = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePdaContent();
            DisposeBuiContent();
            _mirrorViewport?.Dispose();
            _mirrorViewport = null;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Hosts the live <see cref="BoundUserInterface"/> window of any BUI-based device
/// (such as an APC, Atmospheric Alarm, or Static Computer) inside an embedded container
/// so the user can view and interact with it remotely.
/// </summary>
internal sealed partial class RemoteBuiScreenHost : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    public NetEntity TargetNetEntity { get; }
    public Enum UiKey { get; }
    private readonly string _deviceName;

    private EntityUid? _targetUid;
    private BoundUserInterface? _bui;
    private bool _wasBuiOpened;
    private BaseWindow? _pendingWindow;
    private BaseWindow? _embeddedWindow;
    private BoundUserInterfaceState? _latestState;
    private readonly Label _loadingLabel;

    public RemoteBuiScreenHost(NetEntity targetNetEntity, Enum uiKey, string deviceName)
    {
        IoCManager.InjectDependencies(this);

        TargetNetEntity = targetNetEntity;
        UiKey = uiKey;
        _deviceName = deviceName;

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        _loadingLabel = new Label
        {
            Text = $"ESTABLISHING BUI STREAM: {_deviceName}...",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        AddChild(_loadingLabel);

        _uiManager.WindowRoot.OnChildAdded += OnWindowRootChildAdded;
    }

    public void UpdateFromState(RemoteScreenState state)
    {
        if (state.BuiState != null)
            _latestState = state.BuiState;

        TryEnsureBuiAndWindow();
        ApplyLatestState();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        TryEnsureBuiAndWindow();

        if (_pendingWindow != null && !_pendingWindow.Disposed)
        {
            var win = _pendingWindow;
            _pendingWindow = null;
            EmbedWindow(win);
        }

        if (_embeddedWindow is ApcMenu apcMenu)
            apcMenu.SetAccessEnabled(true);
    }

    private void TryEnsureBuiAndWindow()
    {
        if (_targetUid == null || !_entManager.EntityExists(_targetUid.Value))
        {
            if (!_entManager.TryGetEntity(TargetNetEntity, out var resolved) || !resolved.HasValue)
                return;
            _targetUid = resolved.Value;
        }

        var uid = _targetUid.Value;
        if (!_entManager.TryGetComponent<UserInterfaceComponent>(uid, out var uiComp))
            return;

        var uiSys = _entManager.System<SharedUserInterfaceSystem>();

        // Ensure the client BUI is queued/open for the local player
        if (!uiComp.ClientOpenInterfaces.TryGetValue(UiKey, out _bui))
        {
            if (_playerManager.LocalEntity is { } player)
            {
                uiSys.OpenUi((uid, uiComp), UiKey, player, predicted: false);
                uiComp.ClientOpenInterfaces.TryGetValue(UiKey, out _bui);
            }
        }

        if (_bui != null)
            _wasBuiOpened = _bui.IsOpened;

        // If the window was already added to WindowRoot, locate and claim it
        if (_embeddedWindow == null && _pendingWindow == null)
        {
            foreach (var child in _uiManager.WindowRoot.Children)
            {
                if (child is FancyWindow fancy &&
                    !string.IsNullOrEmpty(_deviceName) &&
                    string.Equals(fancy.Title, _deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingWindow = fancy;
                    break;
                }
            }
        }
    }

    private void OnWindowRootChildAdded(Control child)
    {
        if (_embeddedWindow != null || child is not BaseWindow window)
            return;

        if (_targetUid is { } uid &&
            _entManager.TryGetComponent<UserInterfaceComponent>(uid, out var uiComp) &&
            uiComp.ClientOpenInterfaces.TryGetValue(UiKey, out var currentBui))
        {
            _bui = currentBui;
        }

        // When bui.Open() runs, base.Open() sets _bui.IsOpened = true immediately before CreateWindow adds
        // the window to WindowRoot.
        if (_bui != null && _bui.IsOpened && !_wasBuiOpened)
        {
            _wasBuiOpened = true;
            _pendingWindow = window;
            return;
        }

        if (window is FancyWindow fancy &&
            !string.IsNullOrEmpty(_deviceName) &&
            string.Equals(fancy.Title, _deviceName, StringComparison.OrdinalIgnoreCase))
        {
            _pendingWindow = window;
        }
    }

    private void EmbedWindow(BaseWindow window)
    {
        if (window.Disposed)
            return;

        _embeddedWindow = window;
        _loadingLabel.Visible = false;

        // Remove from WindowRoot without triggering BaseWindow.Close()
        window.Parent?.RemoveChild(window);

        window.Resizable = false;
        window.HorizontalExpand = true;
        window.VerticalExpand = true;
        window.HorizontalAlignment = HAlignment.Stretch;
        window.VerticalAlignment = VAlignment.Stretch;
        window.Visible = true;

        if (window is FancyWindow fancy)
        {
            fancy.IsEmbedded = true;
        }

        AddChild(window);
        ApplyLatestState();
    }

    private void ApplyLatestState()
    {
        if (_targetUid is { } uid && _latestState != null)
        {
            var uiSys = _entManager.System<SharedUserInterfaceSystem>();
            uiSys.SetUiState(uid, UiKey, _latestState);
        }

        if (_embeddedWindow is ApcMenu apcMenu)
        {
            if (_latestState is ApcBoundInterfaceState apcState)
                apcMenu.UpdateState(apcState);
            apcMenu.SetAccessEnabled(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiManager.WindowRoot.OnChildAdded -= OnWindowRootChildAdded;

            if (_targetUid is { } uid &&
                _entManager.TryGetComponent<UserInterfaceComponent>(uid, out var uiComp))
            {
                if (_playerManager.LocalEntity is { } player)
                {
                    var uiSys = _entManager.System<SharedUserInterfaceSystem>();
                    uiSys.CloseUi((uid, uiComp), UiKey, player, predicted: false);
                }
                uiComp.ClientOpenInterfaces.Remove(UiKey);
            }

            if (_embeddedWindow != null && !_embeddedWindow.Disposed)
            {
                _embeddedWindow.Orphan();
                _embeddedWindow.Dispose();
                _embeddedWindow = null;
            }
        }

        base.Dispose(disposing);
    }
}
