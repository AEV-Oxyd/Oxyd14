using System.Linq;
using System.Numerics;
using Content.Client.Message;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers;
using Content.Shared.AlertLevel;
using Content.Shared.CartridgeLoader;
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
    Shell
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

public sealed class OsUiFragment : BoxContainer
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
    private readonly Button _navHomeBtn;
    private readonly Button _navProgramsBtn;
    private readonly Button _navSettingsBtn;
    private readonly Button _navShellBtn;

    private readonly BoxContainer _homeView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _programsView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _settingsView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _shellView = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };

    private readonly Label _homeHeaderLabel = new();
    private readonly Label _homeMetricsLabel = new() { FontColorOverride = Color.FromHex("#8a8a90") };
    private readonly ContainerButton _homeDateBtn = new() { HorizontalExpand = true };
    private readonly Label _homeDateLabel = new();
    private readonly ContainerButton _homeTimeBtn = new() { HorizontalExpand = true };
    private readonly Label _homeTimeLabel = new();
    private readonly ContainerButton _homeOwnerBtn = new() { HorizontalExpand = true };
    private readonly Label _homeOwnerLabel = new();
    private readonly ContainerButton _homeIdBtn = new() { HorizontalExpand = true };
    private readonly Label _homeIdLabel = new();
    private readonly ContainerButton _homeStationBtn = new() { HorizontalExpand = true };
    private readonly Label _homeStationLabel = new();
    private readonly ContainerButton _homeAlertBtn = new() { HorizontalExpand = true };
    private readonly Label _homeAlertLabel = new();
    private readonly ContainerButton _homeAlertInstructBtn = new() { HorizontalExpand = true };
    private readonly Label _homeAlertInstructLabel = new();

    private readonly BoxContainer _programsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _settingsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    private readonly Label _shellHeaderLabel = new();
    private readonly Label _shellMetricsLabel = new();
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
        Margin = new Thickness(6);

        var navBar = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _navHomeBtn = new Button { Text = "🏠 Home", HorizontalExpand = true, SetHeight = 28 };
        _navProgramsBtn = new Button { Text = "📱 Programs", HorizontalExpand = true, SetHeight = 28 };
        _navSettingsBtn = new Button { Text = "⚙ Settings", HorizontalExpand = true, SetHeight = 28 };
        _navShellBtn = new Button { Text = "💻 Terminal", HorizontalExpand = true, SetHeight = 28 };

        _navHomeBtn.OnPressed += _ => SetView(OsFragmentView.Home);
        _navProgramsBtn.OnPressed += _ => SetView(OsFragmentView.Programs);
        _navSettingsBtn.OnPressed += _ => SetView(OsFragmentView.Settings);
        _navShellBtn.OnPressed += _ => SetView(OsFragmentView.Shell);

        navBar.AddChild(_navHomeBtn);
        navBar.AddChild(_navProgramsBtn);
        navBar.AddChild(_navSettingsBtn);
        navBar.AddChild(_navShellBtn);
        AddChild(navBar);

        AddChild(_statusLabel);

        // Setup Home View
        _homeDateBtn.AddChild(_homeDateLabel);
        _homeDateBtn.OnPressed += _ => _clipboard.SetText(_cachedDate);
        _homeTimeBtn.AddChild(_homeTimeLabel);
        _homeTimeBtn.OnPressed += _ => _clipboard.SetText(_cachedShift);
        _homeOwnerBtn.AddChild(_homeOwnerLabel);
        _homeOwnerBtn.OnPressed += _ => _clipboard.SetText(_cachedOwner);
        _homeIdBtn.AddChild(_homeIdLabel);
        _homeIdBtn.OnPressed += _ => _clipboard.SetText(_cachedId);
        _homeStationBtn.AddChild(_homeStationLabel);
        _homeStationBtn.OnPressed += _ => _clipboard.SetText(_cachedStation);
        _homeAlertBtn.AddChild(_homeAlertLabel);
        _homeAlertBtn.OnPressed += _ => _clipboard.SetText(_cachedAlert);
        _homeAlertInstructBtn.AddChild(_homeAlertInstructLabel);
        _homeAlertInstructBtn.OnPressed += _ => _clipboard.SetText(_cachedAlertInstructions);

        _homeView.AddChild(_homeHeaderLabel);
        _homeView.AddChild(_homeMetricsLabel);
        _homeView.AddChild(new Label { Text = "----------------------------------------", FontColorOverride = Color.FromHex("#404048"), Margin = new Thickness(0, 4) });
        _homeView.AddChild(_homeDateBtn);
        _homeView.AddChild(_homeTimeBtn);
        _homeView.AddChild(_homeOwnerBtn);
        _homeView.AddChild(_homeIdBtn);
        _homeView.AddChild(_homeStationBtn);
        _homeView.AddChild(_homeAlertBtn);
        _homeView.AddChild(_homeAlertInstructBtn);

        // Setup Programs View
        var progScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        progScroll.AddChild(_programsBox);
        _programsView.AddChild(progScroll);

        // Setup Settings View
        var settingsScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        settingsScroll.AddChild(_settingsBox);
        _settingsView.AddChild(settingsScroll);

        // Setup Shell View
        var shellTopBar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _shellHeaderLabel.HorizontalExpand = true;
        shellTopBar.AddChild(_shellHeaderLabel);
        _remoteToggleBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleRemoteControl));
        shellTopBar.AddChild(_remoteToggleBtn);
        _scanLinksBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ScanAndLinkDevices));
        shellTopBar.AddChild(_scanLinksBtn);
        _shellView.AddChild(shellTopBar);
        _shellView.AddChild(_shellMetricsLabel);

        var cmdRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        var runBtn = new Button { Text = "Execute" };
        runBtn.OnPressed += _ => ExecuteInput();
        _cmdInput.OnTextEntered += _ => ExecuteInput();
        cmdRow.AddChild(_cmdInput);
        cmdRow.AddChild(runBtn);
        _shellView.AddChild(cmdRow);

        var shellScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var shellContent = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        shellContent.AddChild(new Label { Text = "=== Hardware Modules ===", FontColorOverride = Color.Cyan });
        shellContent.AddChild(_hardwareBox);
        shellContent.AddChild(new Label { Text = "=== Linked Device Ports ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        shellContent.AddChild(_linksBox);
        shellScroll.AddChild(shellContent);
        _shellView.AddChild(shellScroll);

        AddChild(_homeView);
        AddChild(_programsView);
        AddChild(_settingsView);
        AddChild(_shellView);

        SetView(OsFragmentView.Home);
        UpdateClock(force: true);
    }

    public void SetView(OsFragmentView view)
    {
        _activeView = view;
        _homeView.Visible = view == OsFragmentView.Home;
        _programsView.Visible = view == OsFragmentView.Programs;
        _settingsView.Visible = view == OsFragmentView.Settings;
        _shellView.Visible = view == OsFragmentView.Shell;

        _navHomeBtn.ModulateSelfOverride = view == OsFragmentView.Home ? Color.Cyan : Color.White;
        _navProgramsBtn.ModulateSelfOverride = view == OsFragmentView.Programs ? Color.Cyan : Color.White;
        _navSettingsBtn.ModulateSelfOverride = view == OsFragmentView.Settings ? Color.Cyan : Color.White;
        _navShellBtn.ModulateSelfOverride = view == OsFragmentView.Shell ? Color.Cyan : Color.White;

        if (view == OsFragmentView.Home)
            UpdateClock(force: true);
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
        _homeDateLabel.Text = $"Station Date: {_cachedDate}";

        var stationTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
        var totalSec = (int)stationTime.TotalSeconds;
        if (!force && totalSec == _lastClockSeconds)
            return;
        _lastClockSeconds = totalSec;
        _cachedShift = stationTime.ToString("hh\\:mm\\:ss");
        var currentLoreTime = _loreBaseTime.Add(stationTime);
        _homeTimeLabel.Text = $"Station Time: {(int)currentLoreTime.Hours:D2}:{currentLoreTime.Minutes:D2}:{currentLoreTime.Seconds:D2} (Shift: {_cachedShift})";
    }

    public void UpdateState(OsUiState state)
    {
        var batText = state.HasBattery ? $" | BAT: {state.BatteryPercent:F0}%" : "";
        var metricsText = $"CPU: {state.CpuLoadPercent}% | RAM: {state.RamUsedMB}/{state.RamTotalMB} MB | DISK: {state.DiskUsedKB}/{state.DiskTotalKB} KB | NET: {state.DownloadBps}/{state.UploadBps} B/s{batText}";

        _homeHeaderLabel.Text = $"NTOS Workstation | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        _homeMetricsLabel.Text = metricsText;
        _shellHeaderLabel.Text = $"NTOS System Shell | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        _shellMetricsLabel.Text = metricsText;
        _remoteToggleBtn.Text = state.AllowRemoteControl ? "Remote: ALLOWED" : "Remote: BLOCKED";
        _statusLabel.Text = state.StatusMessage;

        if (state.PdaOwnerInfo.ActualOwnerName != null)
        {
            _cachedOwner = state.PdaOwnerInfo.ActualOwnerName;
            _homeOwnerLabel.Text = $"Device Owner: {_cachedOwner}";
            _homeOwnerBtn.Visible = true;
        }
        else
        {
            _cachedOwner = "Unknown";
            _homeOwnerBtn.Visible = false;
        }

        if (state.PdaOwnerInfo.IdOwner != null || state.PdaOwnerInfo.JobTitle != null)
        {
            var idName = state.PdaOwnerInfo.IdOwner ?? "Unknown";
            var idJob = state.PdaOwnerInfo.JobTitle ?? "Unassigned";
            _cachedId = $"{idName}, {idJob}";
            _homeIdLabel.Text = $"ID Card: {_cachedId}";
            _homeIdBtn.Visible = true;
        }
        else
        {
            _cachedId = "None Inserted";
            _homeIdLabel.Text = "ID Card: [No ID Inserted]";
            _homeIdBtn.Visible = true;
        }

        _cachedStation = state.StationName ?? "Unknown";
        _homeStationLabel.Text = $"Station: {_cachedStation}";

        if (state.PdaOwnerInfo.StationAlertLevel != null)
        {
            var (alertLvl, _, alertInstructions) = _alert.AlertLevelData(state.PdaOwnerInfo.StationAlertLevel);
            _cachedAlert = alertLvl;
            _cachedAlertInstructions = alertInstructions;
            _homeAlertLabel.Text = $"Alert Level: {alertLvl}";
            _homeAlertLabel.FontColorOverride = state.PdaOwnerInfo.StationAlertColor;
            _homeAlertInstructLabel.Text = $"Instructions: {alertInstructions}";
            _homeAlertBtn.Visible = true;
            _homeAlertInstructBtn.Visible = true;
        }
        else
        {
            _homeAlertBtn.Visible = false;
            _homeAlertInstructBtn.Visible = false;
        }

        _programsBox.RemoveAllChildren();
        foreach (var prog in state.Programs)
        {
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                Margin = new Thickness(0, 2),
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#25252a"),
                    BorderColor = Color.FromHex("#3a3a40"),
                    BorderThickness = new Thickness(1)
                }
            };
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };

            if (prog.Icon != null)
            {
                var iconRect = new AnimatedTextureRect { SetSize = new Vector2(32, 32), VerticalAlignment = VAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                iconRect.SetFromSpriteSpecifier(prog.Icon);
                row.AddChild(iconRect);
            }

            var infoCol = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
            var badge = prog.IsBootProgram ? " [OS / BOOT]" : prog.IsRunning ? " [RUNNING]" : "";
            infoCol.AddChild(new Label { Text = $"{prog.Name}{badge}", FontColorOverride = Color.FromHex("#00D4FF") });
            if (!string.IsNullOrWhiteSpace(prog.Description))
            {
                infoCol.AddChild(new Label { Text = prog.Description, FontColorOverride = Color.FromHex("#8a8a90") });
            }
            row.AddChild(infoCol);

            var btnCol = new BoxContainer { Orientation = LayoutOrientation.Horizontal, VerticalAlignment = VAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            var targetUid = prog.Uid;

            var openBtn = new Button { Text = prog.IsRunning ? "Switch" : "Open", SetWidth = 70, Margin = new Thickness(2, 0) };
            openBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.LaunchProgram, targetUid));
            btnCol.AddChild(openBtn);

            if (prog.IsRunning && !prog.IsBootProgram)
            {
                var closeBtn = new Button { Text = "Stop", SetWidth = 60, Margin = new Thickness(2, 0) };
                closeBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.CloseProgram, targetUid));
                btnCol.AddChild(closeBtn);
            }

            if (prog.InstallationStatus == InstallationStatus.Cartridge)
            {
                var installBtn = new Button { Text = "Install", SetWidth = 70, Margin = new Thickness(2, 0) };
                installBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.InstallProgram, targetUid));
                btnCol.AddChild(installBtn);
            }
            else if (prog.InstallationStatus == InstallationStatus.Installed && !prog.IsBootProgram)
            {
                var uninstallBtn = new Button { Text = "Uninstall", SetWidth = 80, Margin = new Thickness(2, 0) };
                uninstallBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.UninstallProgram, targetUid));
                btnCol.AddChild(uninstallBtn);
            }

            row.AddChild(btnCol);
            panel.AddChild(row);
            _programsBox.AddChild(panel);
        }

        _settingsBox.RemoveAllChildren();
        var ringtoneBtn = new Button { Text = "Change Ringtone", HorizontalExpand = true, Margin = new Thickness(0, 2) };
        ringtoneBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowRingtone));
        _settingsBox.AddChild(ringtoneBtn);

        if (state.CanPlayMusic)
        {
            var musicBtn = new Button { Text = "Synthesizer Music", HorizontalExpand = true, Margin = new Thickness(0, 2) };
            musicBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowMusic));
            _settingsBox.AddChild(musicBtn);
        }

        if (state.HasUplink)
        {
            var uplinkBtn = new Button { Text = "Syndicate Uplink", HorizontalExpand = true, Margin = new Thickness(0, 2), ModulateSelfOverride = Color.Red };
            uplinkBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ShowUplink));
            _settingsBox.AddChild(uplinkBtn);

            var lockUplinkBtn = new Button { Text = "Lock Syndicate Uplink", HorizontalExpand = true, Margin = new Thickness(0, 2) };
            lockUplinkBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.LockUplink));
            _settingsBox.AddChild(lockUplinkBtn);
        }

        var sshBtn = new Button { HorizontalExpand = true, Margin = new Thickness(0, 2) };
        if (state.HasCaptainAccess && !string.IsNullOrEmpty(state.SshPassword))
        {
            sshBtn.Text = $"SSH Password: {state.SshPassword} (Click to copy)";
            sshBtn.OnPressed += _ => _clipboard.SetText(state.SshPassword);
        }
        else
        {
            sshBtn.Text = "SSH Password: [RESTRICTED - Captain Clearance Required]";
            sshBtn.Disabled = true;
        }
        _settingsBox.AddChild(sshBtn);

        var remoteBtn = new Button
        {
            Text = state.AllowRemoteControl ? "Remote Control: ALLOWED" : "Remote Control: BLOCKED",
            HorizontalExpand = true,
            Margin = new Thickness(0, 2),
            ModulateSelfOverride = state.AllowRemoteControl ? Color.Green : Color.Goldenrod
        };
        remoteBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleRemoteControl));
        _settingsBox.AddChild(remoteBtn);

        _settingsBox.AddChild(new Label { Text = "=== Hardware Modules ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 2) });
        if (state.Hardware.Count == 0)
        {
            _settingsBox.AddChild(new Label { Text = "Integrated Motherboard", Margin = new Thickness(4, 0) });
        }
        else
        {
            foreach (var hw in state.Hardware)
            {
                _settingsBox.AddChild(new Label { Text = $"• [{hw.SlotName}] {hw.PartName} ({hw.Size}) - {hw.Details}", Margin = new Thickness(4, 1) });
            }
        }

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
    }

    private void ExecuteInput()
    {
        if (!string.IsNullOrWhiteSpace(_cmdInput.Text))
        {
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
