using System.Linq;
using System.Numerics;
using Content.Client.Message;
using Content.Client.UserInterface.StateRouting;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers;
using Content.Shared.AlertLevel;
using Content.Client.PDA;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.Cartridges;

// Use Content reimplementations derived from ContentControl as the base parent so inputs can be relayed cleanly across remote screens
using BoxContainer = Content.Client.UserInterface.Controls.ContentBoxContainer;
using Button = Content.Client.UserInterface.Controls.ContentButton;
using Label = Content.Client.UserInterface.Controls.ContentLabel;
using LineEdit = Content.Client.UserInterface.Controls.ContentLineEdit;
using ScrollContainer = Content.Client.UserInterface.Controls.ContentScrollContainer;
using PanelContainer = Content.Client.UserInterface.Controls.ContentPanelContainer;
using ContainerButton = Content.Client.UserInterface.Controls.ContentContainerButton;

public enum OsFragmentView
{
    Home,
    Programs,
    Settings,
    Hardware,
    Shell,
    ProgramContent
}
// ============================================================================
// 1. OS Cartridge UI (System Settings, App Launcher, Hardware & DeviceLink)
// ============================================================================

public sealed partial class OsUi : UIFragment
{
    private OsUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OsUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OsUiState osState)
            _fragment?.UpdateState(osState);
    }

    public void SetView(OsFragmentView view) => _fragment?.SetView(view);
}

public sealed class PdaCloseTabButton : ContentContainerButton
{
    private readonly PanelContainer _panel = new();
    private readonly Label _label = new()
    {
        Text = "\u2715",
        FontColorOverride = Color.FromHex("#8a8a8a"),
        HorizontalAlignment = HAlignment.Center,
        VerticalAlignment = VAlignment.Center,
        Margin = new Thickness(0, 0, 0, 2)
    };
    private readonly StyleBoxFlat _styleBox = new()
    {
        BackgroundColor = Color.FromHex("#202023"),
        BorderColor = Color.FromHex("#5a5a5a"),
        BorderThickness = new Thickness(0, 0, 0, 2)
    };

    private bool _isCurrent;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            _isCurrent = value;
            _styleBox.BackgroundColor = Color.FromHex(value ? "#2b2b30" : "#202023");
            _styleBox.BorderThickness = value ? new Thickness(0, 0, 2, 0) : new Thickness(0, 0, 0, 2);
            _label.FontColorOverride = Color.FromHex(value ? "#cfcfd4" : "#8a8a8a");
        }
    }

    public PdaCloseTabButton()
    {
        SetWidth = 20;
        MinHeight = 32;
        _panel.PanelOverride = _styleBox;
        AddChild(_panel);
        AddChild(_label);

        OnMouseEntered += _ =>
        {
            _label.FontColorOverride = Color.FromHex("#ff5555");
            _styleBox.BackgroundColor = Color.FromHex("#3a2023");
        };

        OnMouseExited += _ =>
        {
            _label.FontColorOverride = Color.FromHex(_isCurrent ? "#cfcfd4" : "#8a8a8a");
            _styleBox.BackgroundColor = Color.FromHex(_isCurrent ? "#2b2b30" : "#202023");
        };
    }
}

public sealed class OsUiFragment : BoxContainer, IBuiStateReceiver
{
    [Dependency] private readonly IClipboardManager _clipboard = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystem = default!;

    private readonly AlertLevelSystem _alert;
    private readonly GameTicker _gameTicker;

    private static readonly int _loreMonth;
    private static readonly int _loreDay;
    private static readonly TimeSpan _loreBaseTime;

    static OsUiFragment()
    {
        var rand = new System.Random();
        _loreMonth = rand.Next(1, 13);
        _loreDay = rand.Next(1, 29);
        var baseHour = rand.Next(5, 20);
        var baseMinute = rand.Next(0, 60);
        _loreBaseTime = new TimeSpan(baseHour, baseMinute, 0);
    }

    private readonly Action<OsUiMessageEvent> _send;

    private OsFragmentView _activeView = OsFragmentView.Home;
    private OsUiState? _lastState;
    private bool _showHardware;
    private Control? _activeProgramControl;
    private string? _activeProgramTitle;
    private NetEntity? _activeProgramUid;
    private readonly BoxContainer _runningTabsContainer = new() { Orientation = LayoutOrientation.Horizontal };
    private readonly BoxContainer _programContainer = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Visible = false };

    private readonly PdaNavigationButton _navHomeBtn = new() { SetWidth = 32, CurrentTabBorderThickness = new Thickness(0, 0, 2, 0), IsCurrent = true };
    private readonly PdaNavigationButton _navProgramsBtn = new() { MinWidth = 75 };
    private readonly PdaNavigationButton _navSettingsBtn = new() { MinWidth = 75 };
    private readonly PdaNavigationButton _navShellBtn = new() { MinWidth = 75, CurrentTabBorderThickness = new Thickness(2, 0, 0, 0) };
    private readonly PdaNavigationButton _navHardwareBtn = new() { MinWidth = 75, LabelText = "Hardware", CurrentTabBorderThickness = new Thickness(2, 0, 0, 0) };
    private readonly PdaCloseTabButton _closeHardwareBtn = new() { ToolTip = "Close Hardware tab" };
    private readonly PdaCloseTabButton _closeShellBtn = new() { ToolTip = "Close Terminal tab" };
    private readonly BoxContainer _hardwareTabBox = new() { Orientation = LayoutOrientation.Horizontal, Visible = false, Margin = new Thickness(0) };
    private readonly BoxContainer _shellTabBox = new() { Orientation = LayoutOrientation.Horizontal, Visible = false, Margin = new Thickness(0) };

    private readonly PdaNavigationButton _hwFlashlightBtn = new() { SetWidth = 32, ActiveFgColor = "#EAEFBB", ToolTip = "Toggle Flashlight" };
    private readonly PdaNavigationButton _hwPenBtn = new() { SetWidth = 32, ToolTip = "Eject Pen" };
    private readonly PdaNavigationButton _hwIdBtn = new() { SetWidth = 32, ToolTip = "Eject ID Card" };
    private readonly PdaNavigationButton _hwPaiBtn = new() { SetWidth = 32, ToolTip = "Eject pAI" };
    private readonly PdaNavigationButton _hwDiskBtn = new() { SetWidth = 32, ToolTip = "Eject Disk" };
    private readonly PdaNavigationButton _hwPowerBtn = new() { SetWidth = 32, ToolTip = "Power Off / Close" };

    private readonly BoxContainer _homeView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(8) };
    private readonly BoxContainer _programsView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _settingsView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _shellView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(8) };
    private readonly BoxContainer _hardwareView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(8), Visible = false };

    private readonly Label _homeHeaderLabel = new() { Visible = false };
    private readonly Label _homeMetricsLabel = new() { FontColorOverride = Color.FromHex("#8a8a90"), Visible = false };
    private readonly ContainerButton _homeOwnerBtn = new();
    private readonly RichTextLabel _homeOwnerLabel = new();
    private readonly ContainerButton _homeIdBtn = new();
    private readonly RichTextLabel _homeIdLabel = new();
    private readonly ContainerButton _homeStationBtn = new();
    private readonly RichTextLabel _homeStationLabel = new();
    private readonly ContainerButton _homeAlertBtn = new();
    private readonly RichTextLabel _homeAlertLabel = new();
    private readonly ContainerButton _homeDateBtn = new();
    private readonly RichTextLabel _homeDateLabel = new();
    private readonly ContainerButton _homeTimeBtn = new();
    private readonly RichTextLabel _homeTimeLabel = new();
    private readonly ContainerButton _homeAlertInstructBtn = new();
    private readonly RichTextLabel _homeAlertInstructLabel = new();

    private readonly GridContainer _programsGrid = new() { Columns = 2, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
    private readonly BoxContainer _settingsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    private readonly Label _shellHeaderLabel = new();
    private readonly Label _shellMetricsLabel = new();
    private readonly Label _hardwareHeaderLabel = new();
    private readonly Label _hardwareMetricsLabel = new();
    private readonly Button _remoteToggleBtn = new();
    private readonly Button _scanLinksBtn = new() { Text = "Scan & Link Device Ports" };
    private readonly LineEdit _cmdInput = new() { PlaceHolder = "Run computer / DeviceLink command (devlink scan, power, etc)...", HorizontalExpand = true };
    private readonly BoxContainer _hardwareBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _linksBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen, HorizontalAlignment = HAlignment.Center };

    private string _cachedDate = string.Empty;
    private string _cachedShift = string.Empty;
    private string _cachedOwner = string.Empty;
    private string _cachedId = string.Empty;
    private string _cachedStation = string.Empty;
    private string _cachedAlert = string.Empty;
    private string _cachedAlertInstructions = string.Empty;
    private int _lastClockSeconds = int.MinValue;

    public OsUiFragment(Action<OsUiMessageEvent> send)
    {
        IoCManager.InjectDependencies(this);
        _alert = _entitySystem.GetEntitySystem<AlertLevelSystem>();
        _gameTicker = _entitySystem.GetEntitySystem<GameTicker>();

        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(0);

        var navBar = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinHeight = 32,
            Margin = new Thickness(1, 1, 3, 0)
        };

        _navHomeBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/home.png"));
        _navProgramsBtn.LabelText = Loc.GetString("comp-pda-io-program-list-button");
        _navSettingsBtn.LabelText = Loc.GetString("comp-pda-io-settings-button");
        _navShellBtn.LabelText = "Terminal";
        _navShellBtn.Visible = false;

        _hwFlashlightBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/light.png"));
        _hwPenBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/pencil.png"));
        _hwIdBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/eject.png"));
        _hwPaiBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/pai.png"));
        _hwDiskBtn.IconTexture = new SpriteSpecifier.Rsi(new("/Textures/Oxyd/erisported/_Manual/discs.rsi"), "blue");
        _hwPowerBtn.IconTexture = new SpriteSpecifier.Texture(new("/Textures/Interface/Nano/cross.svg.png"));
        _hwPowerBtn.IconScale = new Vector2(0.5f, 0.5f);

        _navHomeBtn.OnPressed += _ => SetView(OsFragmentView.Home);
        _navProgramsBtn.OnPressed += _ => SetView(OsFragmentView.Programs);
        _navSettingsBtn.OnPressed += _ => SetView(OsFragmentView.Settings);

        _hardwareTabBox.AddChild(_navHardwareBtn);
        _hardwareTabBox.AddChild(_closeHardwareBtn);
        _navHardwareBtn.OnPressed += _ => SetView(OsFragmentView.Hardware);
        _closeHardwareBtn.OnPressed += _ => { _hardwareTabBox.Visible = false; SetView(OsFragmentView.Settings); };

        _shellTabBox.AddChild(_navShellBtn);
        _shellTabBox.AddChild(_closeShellBtn);
        _navShellBtn.OnPressed += _ => SetView(OsFragmentView.Shell);
        _closeShellBtn.OnPressed += _ => { _shellTabBox.Visible = false; SetView(OsFragmentView.Home); };

        _hwFlashlightBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleFlashlight));
        _hwPenBtn.OnPressed += _ =>
        {
            _send(new OsUiMessageEvent(_lastState?.HasPen == true ? OsUiAction.EjectPen : OsUiAction.InsertPen));
        };
        _hwIdBtn.OnPressed += _ =>
        {
            _send(new OsUiMessageEvent(_lastState?.HasId == true ? OsUiAction.EjectId : OsUiAction.InsertId));
        };
        _hwPaiBtn.OnPressed += _ =>
        {
            _send(new OsUiMessageEvent(_lastState?.HasPai == true ? OsUiAction.EjectPai : OsUiAction.InsertPai));
        };
        _hwDiskBtn.OnPressed += _ =>
        {
            _send(new OsUiMessageEvent(_lastState?.HasDisk == true ? OsUiAction.EjectDisk : OsUiAction.InsertDisk));
        };
        _hwPowerBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.PowerOff));

        var baselineFiller = new ContentPanelContainer
        {
            HorizontalExpand = true,
            MinHeight = 32,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#202023"),
                BorderColor = Color.FromHex("#5a5a5a"),
                BorderThickness = new Thickness(0, 0, 0, 2)
            }
        };

        navBar.AddChild(_navHomeBtn);
        navBar.AddChild(_navProgramsBtn);
        navBar.AddChild(_navSettingsBtn);
        navBar.AddChild(_hardwareTabBox);
        navBar.AddChild(_shellTabBox);
        navBar.AddChild(_runningTabsContainer);
        navBar.AddChild(baselineFiller);
        navBar.AddChild(_hwFlashlightBtn);
        navBar.AddChild(_hwPenBtn);
        navBar.AddChild(_hwIdBtn);
        navBar.AddChild(_hwPaiBtn);
        navBar.AddChild(_hwDiskBtn);
        navBar.AddChild(_hwPowerBtn);
        AddChild(navBar);

        AddChild(_statusLabel);

        // Setup Home View
        _homeOwnerBtn.AddChild(_homeOwnerLabel);
        _homeOwnerBtn.OnPressed += _ => _clipboard.SetText(_cachedOwner);
        _homeIdBtn.AddChild(_homeIdLabel);
        _homeIdBtn.OnPressed += _ => _clipboard.SetText(_cachedId);
        _homeStationBtn.AddChild(_homeStationLabel);
        _homeStationBtn.OnPressed += _ => _clipboard.SetText(_cachedStation);
        _homeAlertBtn.AddChild(_homeAlertLabel);
        _homeAlertBtn.OnPressed += _ => _clipboard.SetText(_cachedAlert);
        _homeDateBtn.AddChild(_homeDateLabel);
        _homeDateBtn.OnPressed += _ => _clipboard.SetText(_cachedDate);
        _homeTimeBtn.AddChild(_homeTimeLabel);
        _homeTimeBtn.OnPressed += _ => _clipboard.SetText(_cachedShift);
        _homeAlertInstructBtn.AddChild(_homeAlertInstructLabel);
        _homeAlertInstructBtn.OnPressed += _ => _clipboard.SetText(_cachedAlertInstructions);

        _homeView.AddChild(_homeHeaderLabel);
        _homeView.AddChild(_homeMetricsLabel);
        _homeView.AddChild(_homeOwnerBtn);
        _homeView.AddChild(_homeIdBtn);
        _homeView.AddChild(_homeStationBtn);
        _homeView.AddChild(_homeAlertBtn);
        _homeView.AddChild(_homeDateBtn);
        _homeView.AddChild(_homeTimeBtn);
        _homeView.AddChild(_homeAlertInstructBtn);

        // Setup Programs View
        var progScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = true };
        progScroll.AddChild(_programsGrid);
        _programsView.AddChild(progScroll);

        // Setup Settings View
        var settingsScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = true };
        settingsScroll.AddChild(_settingsBox);
        _settingsView.AddChild(settingsScroll);

        // Setup Shell View
        var shellTopBar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _shellHeaderLabel.HorizontalExpand = true;
        _shellHeaderLabel.FontColorOverride = Color.LightGreen;
        _shellHeaderLabel.Text = "=== System Terminal ===";
        shellTopBar.AddChild(_shellHeaderLabel);
        _shellView.AddChild(shellTopBar);
        _shellView.AddChild(_shellMetricsLabel);

        var cmdRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        var runBtn = new Button { Text = "Execute" };
        runBtn.OnPressed += _ => ExecuteInput();
        _cmdInput.OnTextEntered += _ => ExecuteInput();
        cmdRow.AddChild(_cmdInput);
        cmdRow.AddChild(runBtn);
        _shellView.AddChild(cmdRow);

        _remoteToggleBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleRemoteControl));
        _scanLinksBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ScanAndLinkDevices));

        // Setup Hardware View
        var hwTopBar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _hardwareHeaderLabel.HorizontalExpand = true;
        _hardwareHeaderLabel.FontColorOverride = Color.Cyan;
        _hardwareHeaderLabel.Text = "=== Hardware Configuration ===";
        hwTopBar.AddChild(_hardwareHeaderLabel);
        hwTopBar.AddChild(_remoteToggleBtn);
        hwTopBar.AddChild(_scanLinksBtn);
        _hardwareView.AddChild(hwTopBar);
        _hardwareView.AddChild(_hardwareMetricsLabel);

        var hwScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var hwContent = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        hwContent.AddChild(new Label { Text = "=== Hardware Modules ===", FontColorOverride = Color.Cyan });
        hwContent.AddChild(_hardwareBox);
        hwContent.AddChild(new Label { Text = "=== Linked Device Ports ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        hwContent.AddChild(_linksBox);
        hwScroll.AddChild(hwContent);
        _hardwareView.AddChild(hwScroll);

        AddChild(_homeView);
        AddChild(_programsView);
        AddChild(_settingsView);
        AddChild(_hardwareView);
        AddChild(_shellView);
        AddChild(_programContainer);

        SetView(OsFragmentView.Home);
        UpdateClock(force: true);
    }

    public BuiStateReceiveResult ReceiveBuiState(BoundUserInterfaceState state)
    {
        if (state is OsUiState osState)
        {
            UpdateState(osState);
            return BuiStateReceiveResult.Consumed;
        }
        return BuiStateReceiveResult.Ignored;
    }

    public void SetView(OsFragmentView view)
    {
        _activeView = view;
        _homeView.Visible = view == OsFragmentView.Home;
        _programsView.Visible = view == OsFragmentView.Programs;
        _settingsView.Visible = view == OsFragmentView.Settings;
        _hardwareView.Visible = view == OsFragmentView.Hardware;
        _shellView.Visible = view == OsFragmentView.Shell;
        _programContainer.Visible = view == OsFragmentView.ProgramContent;

        _navHomeBtn.IsCurrent = view == OsFragmentView.Home;
        _navProgramsBtn.IsCurrent = view == OsFragmentView.Programs;
        _navSettingsBtn.IsCurrent = view == OsFragmentView.Settings;
        _navHardwareBtn.IsCurrent = view == OsFragmentView.Hardware;
        _closeHardwareBtn.IsCurrent = view == OsFragmentView.Hardware;
        _navShellBtn.IsCurrent = view == OsFragmentView.Shell;
        _closeShellBtn.IsCurrent = view == OsFragmentView.Shell;

        UpdateRunningTabs();

        if (view == OsFragmentView.Home)
            UpdateClock(force: true);
    }

    public void AttachProgram(Control programControl, string? title, NetEntity? programUid = null)
    {
        if (_activeProgramControl != null && _activeProgramControl != programControl)
        {
            _programContainer.RemoveChild(_activeProgramControl);
        }

        _activeProgramControl = programControl;
        _activeProgramTitle = title;
        if (programUid != null)
            _activeProgramUid = programUid;

        if (programControl.Parent != _programContainer)
        {
            programControl.Parent?.RemoveChild(programControl);
            _programContainer.AddChild(programControl);
        }

        SetView(OsFragmentView.ProgramContent);
    }

    public void DetachProgram(Control programControl)
    {
        if (_programContainer.Children.Contains(programControl))
        {
            _programContainer.RemoveChild(programControl);
        }

        if (_activeProgramControl == programControl)
        {
            _activeProgramControl = null;
            _activeProgramTitle = null;
            if (_activeView == OsFragmentView.ProgramContent)
            {
                SetView(OsFragmentView.Programs);
            }
        }
        UpdateRunningTabs();
    }

    private void UpdateRunningTabs()
    {
        _runningTabsContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        var running = _lastState.Programs.Where(p => p.IsRunning && !p.IsBootProgram).ToList();
        foreach (var prog in running)
        {
            var targetUid = prog.Uid;
            var progTitle = Loc.TryGetString(prog.Name, out var locName) ? locName : prog.Name;

            var tabBox = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                Margin = new Thickness(0)
            };

            var isCur = _activeView == OsFragmentView.ProgramContent && _activeProgramUid == targetUid;
            var tabBtn = new PdaNavigationButton
            {
                LabelText = progTitle,
                MinWidth = 60,
                CurrentTabBorderThickness = new Thickness(2, 0, 0, 0),
                IsCurrent = isCur,
                ToolTip = $"Switch to {progTitle}"
            };

            tabBtn.OnPressed += _ =>
            {
                if (_activeProgramUid == targetUid && _activeProgramControl != null)
                {
                    SetView(OsFragmentView.ProgramContent);
                }
                else
                {
                    _send(new OsUiMessageEvent(OsUiAction.LaunchProgram, targetProgram: targetUid));
                }
            };

            var closeBtn = new PdaCloseTabButton
            {
                IsCurrent = isCur,
                ToolTip = $"Close {progTitle}"
            };
            closeBtn.OnPressed += _ =>
            {
                _send(new OsUiMessageEvent(OsUiAction.CloseProgram, targetProgram: targetUid));
            };

            tabBox.AddChild(tabBtn);
            tabBox.AddChild(closeBtn);
            _runningTabsContainer.AddChild(tabBox);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_activeView == OsFragmentView.Home)
            UpdateClock();
    }

    private void UpdateClock(bool force = false)
    {
        _cachedDate = $"2753-{_loreMonth:D2}-{_loreDay:D2}";
        _homeDateLabel.SetMarkup($"Station Date: [color=white]{_cachedDate}[/color]");

        var stationTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
        var totalSec = (int)stationTime.TotalSeconds;
        if (!force && totalSec == _lastClockSeconds)
            return;
        _lastClockSeconds = totalSec;
        _cachedShift = stationTime.ToString("hh\\:mm\\:ss");
        var currentLoreTime = _loreBaseTime.Add(stationTime);
        _homeTimeLabel.SetMarkup($"Station Time: [color=white]{(int)currentLoreTime.Hours:D2}:{currentLoreTime.Minutes:D2}:{currentLoreTime.Seconds:D2}[/color] (Shift: {_cachedShift})");
    }

    public void UpdateState(OsUiState state)
    {
        _lastState = state;

        if (state.ActiveProgram != null)
        {
            _activeProgramUid = state.ActiveProgram;
        }

        var batText = state.HasBattery ? $" | BAT: {state.BatteryPercent:F0}%" : "";
        var metricsText = $"CPU: {state.CpuLoadPercent}% | RAM: {state.RamUsedMB}/{state.RamTotalMB} MB | DISK: {state.DiskUsedKB}/{state.DiskTotalKB} KB | NET: {state.DownloadBps}/{state.UploadBps} B/s{batText}";

        if (state.Template != ComputerUiTemplate.Pda)
        {
            _homeHeaderLabel.Visible = true;
            _homeHeaderLabel.Text = $"NTOS Workstation | GUID: {state.DeviceGuid} | Mode: {state.Template}";
            _homeMetricsLabel.Visible = true;
            _homeMetricsLabel.Text = metricsText;
        }
        else
        {
            _homeHeaderLabel.Visible = false;
            _homeMetricsLabel.Visible = false;
        }
        _homeHeaderLabel.Text = $"NTOS Workstation | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        _homeMetricsLabel.Text = metricsText;
        _shellHeaderLabel.Text = $"NTOS System Shell | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        _shellMetricsLabel.Text = metricsText;
        _hardwareHeaderLabel.Text = $"NTOS Hardware Configuration | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        _hardwareMetricsLabel.Text = metricsText;
        _remoteToggleBtn.Text = state.AllowRemoteControl ? "Remote: ALLOWED" : "Remote: BLOCKED";
        _statusLabel.Text = state.StatusMessage;

        _hwFlashlightBtn.Visible = state.HasFlashlight || state.Template == ComputerUiTemplate.Pda;
        _hwFlashlightBtn.IsActive = state.FlashlightEnabled;

        _hwPenBtn.Visible = state.Template == ComputerUiTemplate.Pda || state.HasPen;
        _hwPenBtn.IsActive = state.HasPen;

        _hwIdBtn.Visible = state.Template == ComputerUiTemplate.Pda || state.HasId;
        _hwIdBtn.IsActive = state.HasId;
        _hwIdBtn.ToolTip = state.HasId ? "Eject Inserted ID" : "Insert ID Card";

        _hwPaiBtn.Visible = state.Template == ComputerUiTemplate.Pda || state.HasPai;
        _hwPaiBtn.IsActive = state.HasPai;

        _hwDiskBtn.Visible = state.Template != ComputerUiTemplate.Embedded;
        _hwDiskBtn.IsActive = state.HasDisk;
        _hwDiskBtn.ToolTip = state.HasDisk ? $"Eject {state.DiskName ?? "Disk"}" : "Insert Data Disk";

        _hwPowerBtn.Visible = true;

        if (state.PdaOwnerInfo.ActualOwnerName != null)
        {
            _cachedOwner = state.PdaOwnerInfo.ActualOwnerName;
            _homeOwnerLabel.SetMarkup(Loc.GetString("comp-pda-ui-owner",
                ("actualOwnerName", FormattedMessage.RemoveMarkupPermissive(_cachedOwner))));
            _homeOwnerBtn.Visible = true;
        }
        else
        {
            _cachedOwner = "Unknown";
            _homeOwnerBtn.Visible = false;
        }

        if (state.PdaOwnerInfo.IdOwner != null || state.PdaOwnerInfo.JobTitle != null)
        {
            var idName = state.PdaOwnerInfo.IdOwner ?? Loc.GetString("comp-pda-ui-unknown");
            var idJob = state.PdaOwnerInfo.JobTitle ?? Loc.GetString("comp-pda-ui-unassigned");
            _cachedId = $"{idName}, {idJob}";
            _homeIdLabel.SetMarkup(Loc.GetString("comp-pda-ui",
                ("owner", FormattedMessage.RemoveMarkupPermissive(idName)),
                ("jobTitle", FormattedMessage.RemoveMarkupPermissive(idJob))));
            _homeIdBtn.Visible = true;
        }
        else
        {
            _cachedId = Loc.GetString("comp-pda-ui-blank");
            _homeIdLabel.SetMarkup(Loc.GetString("comp-pda-ui-blank"));
            _homeIdBtn.Visible = true;
        }

        _cachedStation = state.StationName ?? Loc.GetString("comp-pda-ui-unknown");
        _homeStationLabel.SetMarkup(Loc.GetString("comp-pda-ui-station",
            ("station", _cachedStation)));

        UpdateClock();

        if (state.PdaOwnerInfo.StationAlertLevel != null)
        {
            var (alertLvl, _, alertInstructions) = _alert.AlertLevelData(state.PdaOwnerInfo.StationAlertLevel);
            _cachedAlert = alertLvl;
            _cachedAlertInstructions = alertInstructions;
            var alertColor = state.PdaOwnerInfo.StationAlertColor;
            _homeAlertLabel.SetMarkup(Loc.GetString(
                "comp-pda-ui-station-alert-level",
                ("color", alertColor),
                ("level", alertLvl)
            ));
            _homeAlertInstructLabel.SetMarkup(Loc.GetString(
                "comp-pda-ui-station-alert-level-instructions",
                ("instructions", alertInstructions))
            );
            _homeAlertBtn.Visible = true;
            _homeAlertInstructBtn.Visible = true;
        }
        else
        {
            _homeAlertBtn.Visible = false;
            _homeAlertInstructBtn.Visible = false;
        }

        _programsGrid.RemoveAllChildren();
        if (state.Programs.Count == 0)
        {
            _programsGrid.AddChild(new Label
            {
                Text = Loc.GetString("comp-pda-io-no-programs-available"),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                VerticalExpand = true
            });
        }
        else
        {
            for (var i = 0; i < state.Programs.Count; i++)
            {
                var prog = state.Programs[i];
                var targetUid = prog.Uid;
                var item = new ContainerButton
                {
                    HorizontalExpand = true,
                    MinHeight = 60,
                    Margin = new Thickness(4)
                };

                var panel = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = Color.FromHex("#25252a")
                    }
                };
                item.AddChild(panel);

                var rootBox = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    VerticalExpand = true
                };

                var iconCol = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    VerticalExpand = true,
                    MinWidth = 60
                };
                var iconRect = new AnimatedTextureRect
                {
                    HorizontalAlignment = HAlignment.Center,
                    VerticalAlignment = VAlignment.Center,
                    VerticalExpand = true
                };
                if (prog.Icon != null)
                    iconRect.SetFromSpriteSpecifier(prog.Icon);
                iconCol.AddChild(iconRect);
                rootBox.AddChild(iconCol);

                var infoCol = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    VerticalExpand = true,
                    HorizontalExpand = true
                };
                var progTitle = Loc.TryGetString(prog.Name, out var locName) ? locName : prog.Name;
                var badge = prog.IsBootProgram ? " [OS]" : prog.IsRunning ? " [RUNNING]" : "";
                infoCol.AddChild(new Label
                {
                    Text = $"{progTitle}{badge}",
                    VerticalExpand = true,
                    FontColorOverride = prog.IsRunning ? Color.FromHex("#00D4FF") : Color.White
                });

                var btnRow = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    SetHeight = 28,
                    Margin = new Thickness(0, 0, 4, 4)
                };
                btnRow.AddChild(new ContentControl { HorizontalExpand = true });

                if (prog.InstallationStatus == InstallationStatus.Cartridge)
                {
                    var installBtn = new Button
                    {
                        Text = Loc.GetString("cartridge-bound-user-interface-install-button"),
                        SetWidth = 90,
                        HorizontalAlignment = HAlignment.Right,
                        VerticalExpand = true
                    };
                    installBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.InstallProgram, targetUid));
                    btnRow.AddChild(installBtn);
                }
                else if (prog.InstallationStatus == InstallationStatus.Installed && !prog.IsBootProgram)
                {
                    var uninstallBtn = new Button
                    {
                        Text = "Uninstall",
                        SetWidth = 90,
                        HorizontalAlignment = HAlignment.Right,
                        VerticalExpand = true
                    };
                    uninstallBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.UninstallProgram, targetUid));
                    btnRow.AddChild(uninstallBtn);
                }
                infoCol.AddChild(btnRow);
                rootBox.AddChild(infoCol);

                item.AddChild(rootBox);
                item.OnPressed += _ =>
                {
                    if (_activeProgramUid == targetUid && _activeProgramControl != null)
                    {
                        SetView(OsFragmentView.ProgramContent);
                    }
                    else
                    {
                        _send(new OsUiMessageEvent(OsUiAction.LaunchProgram, targetUid));
                    }
                };
                _programsGrid.AddChild(item);
            }

            if (state.Programs.Count % 2 != 0)
            {
                _programsGrid.AddChild(new ContentControl { HorizontalExpand = true });
            }
        }

        _settingsBox.RemoveAllChildren();
        var ringtoneBtn = new PdaSettingsButton
        {
            Text = Loc.GetString("comp-pda-ui-ringtone-button"),
            Description = Loc.GetString("comp-pda-ui-ringtone-button-description")
        };
        ringtoneBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowRingtone));
        _settingsBox.AddChild(ringtoneBtn);

        if (state.CanPlayMusic)
        {
            var musicBtn = new PdaSettingsButton
            {
                Text = Loc.GetString("pda-bound-user-interface-music-button"),
                Description = Loc.GetString("pda-bound-user-interface-music-button-description")
            };
            musicBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowMusic));
            _settingsBox.AddChild(musicBtn);
        }

        if (state.HasUplink)
        {
            var uplinkBtn = new PdaSettingsButton
            {
                Text = Loc.GetString("pda-bound-user-interface-show-uplink-title"),
                Description = Loc.GetString("pda-bound-user-interface-show-uplink-description"),
                ForegroundColor = Color.Red
            };
            uplinkBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowUplink));
            _settingsBox.AddChild(uplinkBtn);

            var lockUplinkBtn = new PdaSettingsButton
            {
                Text = Loc.GetString("pda-bound-user-interface-lock-uplink-title"),
                Description = Loc.GetString("pda-bound-user-interface-lock-uplink-description")
            };
            lockUplinkBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.LockUplink));
            _settingsBox.AddChild(lockUplinkBtn);
        }

        var sshBtn = new PdaSettingsButton();
        if (state.HasCaptainAccess && !string.IsNullOrEmpty(state.SshPassword))
        {
            sshBtn.Text = $"SSH Password: {state.SshPassword}";
            sshBtn.Description = "Click to copy SSH administrative password to clipboard.";
            sshBtn.OnPressed += _ => _clipboard.SetText(state.SshPassword);
        }
        else
        {
            sshBtn.Text = "SSH Password: [RESTRICTED]";
            sshBtn.Description = "Captain Clearance Required: Insert an authorized ID card to view SSH credentials.";
            sshBtn.Disabled = true;
        }
        _settingsBox.AddChild(sshBtn);

        var remoteBtn = new PdaSettingsButton
        {
            Text = state.AllowRemoteControl ? "Remote Control: ALLOWED" : "Remote Control: BLOCKED",
            Description = state.AllowRemoteControl
                ? "Remote desktop connections are permitted. Click to block."
                : "Remote desktop connections are blocked. Click to allow.",
            ForegroundColor = state.AllowRemoteControl ? Color.Green : Color.Goldenrod
        };
        remoteBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleRemoteControl));
        _settingsBox.AddChild(remoteBtn);

        if (state.HasFlashlight || state.Template == ComputerUiTemplate.Pda)
        {
            var flBtn = new PdaSettingsButton
            {
                Text = state.FlashlightEnabled ? "Toggle Flashlight [Status: ON]" : "Toggle Flashlight [Status: OFF]",
                Description = "Toggle the built-in illumination flashlight.",
                ForegroundColor = state.FlashlightEnabled ? Color.Yellow : Color.White
            };
            flBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleFlashlight));
            _settingsBox.AddChild(flBtn);
        }
        if (state.Template == ComputerUiTemplate.Pda || state.HasId)
        {
            var ejectId = new PdaSettingsButton
            {
                Text = state.HasId ? "Eject ID Card" : "Insert ID Card",
                Description = state.HasId ? "Eject the currently inserted identity card." : "Insert an identity card into the reader slot.",
                Disabled = false
            };
            ejectId.OnPressed += _ => _send(new OsUiMessageEvent(state.HasId ? OsUiAction.EjectId : OsUiAction.InsertId));
            _settingsBox.AddChild(ejectId);
        }
        if (state.Template == ComputerUiTemplate.Pda || state.HasPen)
        {
            var ejectPen = new PdaSettingsButton
            {
                Text = state.HasPen ? "Eject Pen" : "Insert Pen",
                Description = state.HasPen ? "Eject the pen or stylus from the accessory slot." : "Insert a pen or stylus into the accessory slot.",
                Disabled = false
            };
            ejectPen.OnPressed += _ => _send(new OsUiMessageEvent(state.HasPen ? OsUiAction.EjectPen : OsUiAction.InsertPen));
            _settingsBox.AddChild(ejectPen);
        }
        if (state.Template == ComputerUiTemplate.Pda || state.HasPai)
        {
            var ejectPai = new PdaSettingsButton
            {
                Text = state.HasPai ? "Eject Personal AI (pAI)" : "Insert Personal AI",
                Description = state.HasPai ? "Eject the personal AI card from the device." : "Insert a personal AI card into the device slot.",
                Disabled = false
            };
            ejectPai.OnPressed += _ => _send(new OsUiMessageEvent(state.HasPai ? OsUiAction.EjectPai : OsUiAction.InsertPai));
            _settingsBox.AddChild(ejectPai);
        }
        if (state.Template != ComputerUiTemplate.Embedded)
        {
            var diskTxt = state.HasDisk ? $"Eject Disk ({state.DiskName ?? "Inserted"})" : "Insert Disk";
            var ejectDisk = new PdaSettingsButton
            {
                Text = diskTxt,
                Description = state.HasDisk ? "Eject the inserted data disk." : "Insert a data disk into the drive slot.",
                Disabled = false
            };
            ejectDisk.OnPressed += _ => _send(new OsUiMessageEvent(state.HasDisk ? OsUiAction.EjectDisk : OsUiAction.InsertDisk));
            _settingsBox.AddChild(ejectDisk);
        }

        var hwSettingsBtn = new PdaSettingsButton
        {
            Text = "Hardware",
            Description = "Inspect motherboard slots, components, and hardware modules."
        };
        hwSettingsBtn.OnPressed += _ =>
        {
            _hardwareTabBox.Visible = true;
            SetView(OsFragmentView.Hardware);
        };
        _settingsBox.AddChild(hwSettingsBtn);

        _hardwareBox.RemoveAllChildren();
        if (state.Hardware.Count == 0)
        {
            _hardwareBox.AddChild(new Label { Text = "Integrated System Board" });
        }
        else
        {
            foreach (var hw in state.Hardware)
            {
                _hardwareBox.AddChild(new Label { Text = $"[{hw.SlotName}] {hw.PartName} ({hw.Size}) - {hw.Details}" });
            }
        }

        _linksBox.RemoveAllChildren();
        if (state.LinkedPorts.Count == 0)
        {
            _linksBox.AddChild(new Label { Text = "No local DeviceLink ports connected." });
        }
        else
        {
            foreach (var link in state.LinkedPorts)
            {
                _linksBox.AddChild(new Label { Text = $"{link.SourcePort} -> {link.TargetEntityName} ({link.SinkPort})" });
            }
        }

        UpdateRunningTabs();
    }

    private void ExecuteInput()
    {
        if (!string.IsNullOrWhiteSpace(_cmdInput.Text))
        {
            var text = _cmdInput.Text.Trim();
            if (string.Equals(text, "hardware", StringComparison.OrdinalIgnoreCase))
            {
                _hardwareTabBox.Visible = true;
                SetView(OsFragmentView.Hardware);
                _cmdInput.Clear();
                return;
            }
            if (string.Equals(text, "terminal", StringComparison.OrdinalIgnoreCase))
            {
                _shellTabBox.Visible = true;
                SetView(OsFragmentView.Shell);
                _cmdInput.Clear();
                return;
            }
            _send(new OsUiMessageEvent(OsUiAction.RunCommand, command: _cmdInput.Text));
            _cmdInput.Clear();
        }
    }
}

// ============================================================================
// 2. Router Management UI (Connections, Blacklist, Packet Logs, DNS)
// ============================================================================

public sealed partial class RouterUi : UIFragment
{
    private RouterUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new RouterUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is RouterUiState routerState)
            _fragment?.UpdateState(routerState);
    }
}

public sealed class RouterUiFragment : BoxContainer
{
    private readonly Action<RouterUiMessageEvent> _send;
    private readonly Label _titleLabel = new();
    private readonly LineEdit _blacklistInput = new() { PlaceHolder = "Enter Device GUID to Blacklist...", HorizontalExpand = true };
    private readonly BoxContainer _connectionsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _blacklistBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _packetsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _devicesBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public RouterUiFragment(Action<RouterUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _titleLabel.HorizontalExpand = true;
        header.AddChild(_titleLabel);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.Refresh));
        header.AddChild(refreshBtn);

        var clearBtn = new Button { Text = "Clear Logs" };
        clearBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.ClearLogs));
        header.AddChild(clearBtn);
        AddChild(header);

        var blRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        var blBtn = new Button { Text = "Blacklist GUID & Sever Connections" };
        blBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrWhiteSpace(_blacklistInput.Text))
            {
                _send(new RouterUiMessageEvent(RouterUiAction.BlacklistGuid, _blacklistInput.Text));
                _blacklistInput.Clear();
            }
        };
        blRow.AddChild(_blacklistInput);
        blRow.AddChild(blBtn);
        AddChild(blRow);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

        body.AddChild(new Label { Text = "=== Active Connections (Downloads / Uploads / SSH / Screen Shares) ===", FontColorOverride = Color.Cyan });
        body.AddChild(_connectionsBox);

        body.AddChild(new Label { Text = "=== Blacklisted GUIDs ===", FontColorOverride = Color.Red, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_blacklistBox);

        body.AddChild(new Label { Text = "=== Connected Devices & DNS ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_devicesBox);

        body.AddChild(new Label { Text = "=== Recorded Network Packets ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_packetsBox);

        scroll.AddChild(body);
        AddChild(scroll);
    }

    public void UpdateState(RouterUiState state)
    {
        _titleLabel.Text = $"Station Network Router | GUID: {state.RouterGuid}";

        _connectionsBox.RemoveAllChildren();
        if (state.ActiveConnections.Count == 0)
        {
            _connectionsBox.AddChild(new Label { Text = "No active connections." });
        }
        else
        {
            foreach (var conn in state.ActiveConnections)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label
                {
                    Text = $"[{conn.ConnectionType}] {conn.SourceGuid} -> {conn.TargetGuid} ({conn.Description})",
                    HorizontalExpand = true
                });

                var closeBtn = new Button { Text = "Close Connection" };
                var connId = conn.ConnectionId;
                closeBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.CloseConnection, connId));
                row.AddChild(closeBtn);

                var blSourceBtn = new Button { Text = $"Blacklist {conn.SourceGuid}" };
                var srcGuid = conn.SourceGuid;
                blSourceBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.BlacklistGuid, srcGuid));
                row.AddChild(blSourceBtn);

                _connectionsBox.AddChild(row);
            }
        }

        _blacklistBox.RemoveAllChildren();
        if (state.BlacklistedGuids.Count == 0)
        {
            _blacklistBox.AddChild(new Label { Text = "No blacklisted GUIDs." });
        }
        else
        {
            foreach (var guid in state.BlacklistedGuids)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label { Text = $"BLOCKED: {guid}", HorizontalExpand = true });
                var unblBtn = new Button { Text = "Unblock" };
                var g = guid;
                unblBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.UnblacklistGuid, g));
                row.AddChild(unblBtn);
                _blacklistBox.AddChild(row);
            }
        }

        _devicesBox.RemoveAllChildren();
        foreach (var (dnsName, dnsGuid) in state.DnsRecords)
        {
            _devicesBox.AddChild(new Label { Text = $"DNS '{dnsName}' -> {dnsGuid}", FontColorOverride = Color.LightGreen });
        }
        _devicesBox.AddChild(new Label { Text = $"Peers ({state.ConnectedDevices.Count}): {string.Join(", ", state.ConnectedDevices)}" });

        _packetsBox.RemoveAllChildren();
        for (var i = state.PacketLogs.Count - 1; i >= 0; i--)
        {
            var pkt = state.PacketLogs[i];
            _packetsBox.AddChild(new Label
            {
                Text = $"#{pkt.PacketId} [{pkt.PacketType}] {pkt.SenderGuid} -> {pkt.TargetGuid}: {pkt.DecipheredSummary}",
                FontColorOverride = pkt.IsEncrypted ? Color.Orange : Color.White
            });
        }
    }
}

// ============================================================================
// 3. Packet Sniffer & Re-emitter UI
// ============================================================================

public sealed partial class PacketSnifferUi : UIFragment
{
    private PacketSnifferUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new PacketSnifferUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PacketSnifferUiState snifferState)
            _fragment?.UpdateState(snifferState);
    }
}

public sealed class PacketSnifferUiFragment : BoxContainer
{
    private readonly Action<PacketSnifferUiMessageEvent> _send;
    private readonly Button _toggleBtn = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly LineEdit _targetGuidInput = new() { PlaceHolder = "Override Target GUID (optional)", HorizontalExpand = true };
    private readonly LineEdit _payloadInput = new() { PlaceHolder = "Modified payload for unencrypted packet re-emission...", HorizontalExpand = true };
    private readonly BoxContainer _packetList = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public PacketSnifferUiFragment(Action<PacketSnifferUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var topRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _toggleBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.ToggleCapture));
        topRow.AddChild(_toggleBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.Refresh));
        topRow.AddChild(refreshBtn);

        var clearBtn = new Button { Text = "Clear Buffer" };
        clearBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.Clear));
        topRow.AddChild(clearBtn);
        AddChild(topRow);

        AddChild(_statusLabel);
        AddChild(_targetGuidInput);
        AddChild(_payloadInput);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(0, 6, 0, 0) };
        scroll.AddChild(_packetList);
        AddChild(scroll);
    }

    public void UpdateState(PacketSnifferUiState state)
    {
        _toggleBtn.Text = state.IsCapturing ? "Capture: ACTIVE" : "Capture: PAUSED";
        _statusLabel.Text = state.StatusMessage;

        _packetList.RemoveAllChildren();
        for (var i = state.Packets.Count - 1; i >= 0; i--)
        {
            var pkt = state.Packets[i];
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            var summary = new Label
            {
                Text = $"#{pkt.PacketId} [{pkt.PacketType}] {pkt.SenderGuid}->{pkt.TargetGuid} {(pkt.IsEncrypted ? "[ENC]" : "[PLAIN]")}: {pkt.PayloadPreview}",
                HorizontalExpand = true,
                FontColorOverride = pkt.IsEncrypted ? Color.Orange : Color.LightGreen
            };
            row.AddChild(summary);

            var reemitBtn = new Button
            {
                Text = pkt.IsEncrypted ? "Replay Encrypted" : "Modify & Re-emit"
            };
            var pktId = pkt.PacketId;
            reemitBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(
                PacketSnifferUiAction.ReemitPacket,
                pktId,
                _targetGuidInput.Text,
                _payloadInput.Text));
            row.AddChild(reemitBtn);

            _packetList.AddChild(row);
        }
    }
}

// ============================================================================
// 4. Autolathe DeviceLink Control UI
// ============================================================================

public sealed partial class LatheControlUi : UIFragment
{
    private LatheControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new LatheControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is LatheControlUiState latheState)
            _fragment?.UpdateState(latheState);
    }
}

public sealed class LatheControlUiFragment : BoxContainer
{
    private readonly Action<LatheControlUiMessageEvent> _send;
    private readonly Label _headerLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly Label _materialsLabel = new();
    private readonly Label _queueLabel = new();
    private readonly BoxContainer _designsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public LatheControlUiFragment(Action<LatheControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var actions = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link Lathe" };
        linkBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.LinkLathe));
        actions.AddChild(linkBtn);

        var syncBtn = new Button { Text = "Load Disk Designs" };
        syncBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.SyncDiskDesigns));
        actions.AddChild(syncBtn);

        var cancelBtn = new Button { Text = "Cancel Order" };
        cancelBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.CancelQueue));
        actions.AddChild(cancelBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.Refresh));
        actions.AddChild(refreshBtn);

        AddChild(actions);
        AddChild(_headerLabel);
        AddChild(_statusLabel);
        AddChild(_materialsLabel);
        AddChild(_queueLabel);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(0, 6, 0, 0) };
        scroll.AddChild(_designsBox);
        AddChild(scroll);
    }

    public void UpdateState(LatheControlUiState state)
    {
        _headerLabel.Text = $"Lathe: {state.LatheName} | Producing: {state.CurrentProducing}";
        _statusLabel.Text = state.StatusMessage;

        var matParts = new List<string>();
        foreach (var (mat, amt) in state.Materials)
        {
            matParts.Add($"{mat}: {amt}");
        }
        _materialsLabel.Text = matParts.Count > 0 ? $"Materials: {string.Join(" | ", matParts)}" : "Materials: None";
        _queueLabel.Text = state.QueueItems.Count > 0 ? $"Queue: {string.Join(", ", state.QueueItems)}" : "Queue: Empty";

        _designsBox.RemoveAllChildren();
        foreach (var design in state.Designs)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label
            {
                Text = $"{design.Name} ({design.MaterialsSummary})",
                HorizontalExpand = true,
                FontColorOverride = design.CanProduce ? Color.White : Color.Gray
            });

            var printBtn = new Button { Text = "Print 1x", Disabled = !design.CanProduce };
            var designId = design.Id;
            printBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.PrintDesign, designId, 1));
            row.AddChild(printBtn);

            _designsBox.AddChild(row);
        }
    }
}

// ============================================================================
// 5. APC DeviceLink Control UI
// ============================================================================

public sealed partial class ApcControlUi : UIFragment
{
    private ApcControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new ApcControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is ApcControlUiState apcState)
            _fragment?.UpdateState(apcState);
    }
}

public sealed class ApcControlUiFragment : BoxContainer
{
    private readonly Action<ApcControlUiMessageEvent> _send;
    private readonly Label _apcNameLabel = new();
    private readonly Label _breakerLabel = new();
    private readonly Label _chargeLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly Button _toggleBreakerBtn = new() { Text = "Toggle Main Breaker (via Console Command)" };

    public ApcControlUiFragment(Action<ApcControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link APC" };
        linkBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.LinkApc));
        row.AddChild(linkBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.Refresh));
        row.AddChild(refreshBtn);

        AddChild(row);
        AddChild(_apcNameLabel);
        AddChild(_breakerLabel);
        AddChild(_chargeLabel);
        AddChild(_toggleBreakerBtn);
        AddChild(_statusLabel);

        _toggleBreakerBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.ToggleMainBreaker));
    }

    public void UpdateState(ApcControlUiState state)
    {
        _apcNameLabel.Text = $"Target APC: {state.ApcName}";
        _breakerLabel.Text = $"Main Breaker: {(state.MainBreakerEnabled ? "ONLINE" : "TRIPPED / OFF")}";
        _chargeLabel.Text = $"Battery Charge: {state.ChargePercent:F1}% | External Power: {state.ExternalPowerState}";
        _statusLabel.Text = state.StatusMessage;
    }
}

// ============================================================================
// 6. Atmos (Air Alarm) DeviceLink Control UI
// ============================================================================

public sealed partial class AtmosControlUi : UIFragment
{
    private AtmosControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new AtmosControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is AtmosControlUiState atmosState)
            _fragment?.UpdateState(atmosState);
    }
}

public sealed class AtmosControlUiFragment : BoxContainer
{
    private readonly Action<AtmosControlUiMessageEvent> _send;
    private readonly Label _alarmNameLabel = new();
    private readonly Label _telemetryLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };

    public AtmosControlUiFragment(Action<AtmosControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var topRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link Air Alarm" };
        linkBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.LinkAlarm));
        topRow.AddChild(linkBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.Refresh));
        topRow.AddChild(refreshBtn);
        AddChild(topRow);

        AddChild(_alarmNameLabel);
        AddChild(_telemetryLabel);
        AddChild(_modeLabel);

        var modesRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 6) };
        var filterBtn = new Button { Text = "Filtering" };
        filterBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeFiltering));
        modesRow.AddChild(filterBtn);

        var fillBtn = new Button { Text = "Fill" };
        fillBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeFill));
        modesRow.AddChild(fillBtn);

        var panicBtn = new Button { Text = "Panic Siphon" };
        panicBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModePanic));
        modesRow.AddChild(panicBtn);

        var offBtn = new Button { Text = "Off" };
        offBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeNone));
        modesRow.AddChild(offBtn);

        AddChild(modesRow);
        AddChild(_statusLabel);
    }

    public void UpdateState(AtmosControlUiState state)
    {
        _alarmNameLabel.Text = $"Target Air Alarm: {state.AlarmName} [{state.DangerLevel}]";
        _telemetryLabel.Text = $"Pressure: {state.PressureKpa:F1} kPa | Temperature: {state.TemperatureKelvin:F1} K";
        _modeLabel.Text = $"Operating Mode: {state.CurrentMode}";
        _statusLabel.Text = state.StatusMessage;
    }
}
