using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Specialized Station Network Core OS UI (<c>/bin/netcore_os.nte</c>) running on <c>StationNetworkRouter</c>.
/// Hosts the Core Auto-Sync dashboard alongside child system programs (Router Admin, Terminal, File Browser, Packet Sniffer).
/// </summary>
public sealed partial class NetworkCoreOsUiControl : ProgramUiControl, IOsProgramUiHost
{
    [Dependency] private IEntityManager _entMan = default!;

    private readonly ProgramIconResolver _icons;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _hwLabel;
    private readonly ContentWrapContainer _unifiedNavBar;
    private readonly ContentButton _coreTabBtn;
    private readonly ContentButton _installedTabBtn;
    private readonly ContentButton _refreshBtn;
    private readonly List<Control> _inlineRunningTabs = new();

    private readonly ContentBoxContainer _coreView;
    private readonly ContentLabel _dataServerLabel;
    private readonly ContentLabel _statsSummaryLabel;
    private readonly ContentLabel _lastSyncLabel;
    private readonly ContentButton _autoSyncToggleBtn;
    private readonly ContentButton _forceSyncBtn;
    private readonly ContentBoxContainer _syncLogsContainer;

    private readonly ContentBoxContainer _childViewport;
    private readonly ContentBoxContainer _installedView;
    private readonly ContentBoxContainer _installedContainer;

    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private NetEntity? _activeChildNet;

    public NetworkCoreOsUiControl()
    {
        IoCManager.InjectDependencies(this);
        _icons = new ProgramIconResolver(
            _entMan,
            IoCManager.Resolve<IPrototypeManager>(),
            IoCManager.Resolve<IResourceCache>(),
            IoCManager.Resolve<IComponentFactory>());
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 5
        };

        var headerPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1926"),
                BorderColor = Color.FromHex("#00A8CC"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };
        var headerBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2
        };
        _headerLabel = new ContentLabel
        {
            Name = "NetworkCoreHeaderLabel",
            Text = "STATION NETWORK CORE OS",
            FontColorOverride = Color.FromHex("#00E5FF"),
            HorizontalExpand = true
        };
        _hwLabel = new ContentLabel
        {
            Text = "Initializing core telemetry...",
            FontColorOverride = Color.FromHex("#88B8CC"),
            HorizontalExpand = true
        };
        headerBox.AddChild(_headerLabel);
        headerBox.AddChild(_hwLabel);
        headerPanel.AddChild(headerBox);
        root.AddChild(headerPanel);

        // Single Unified Navigation + Running Apps Bar
        _unifiedNavBar = new ContentWrapContainer
        {
            Name = "NetworkCoreNavBar",
            HorizontalSeparation = 5,
            VerticalSeparation = 4
        };
        _coreTabBtn = new ContentButton { Name = "NetCoreTabCoreBtn", Text = "Core & Sync", MinWidth = 96 };
        _coreTabBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.SetTab, tabIndex: 0));

        _installedTabBtn = new ContentButton { Name = "NetCoreTabAppsBtn", Text = "Apps", MinWidth = 64 };
        _installedTabBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.SetTab, tabIndex: 2));

        _refreshBtn = new ContentButton { Text = "Refresh", MinWidth = 68 };
        _refreshBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.Refresh));

        _unifiedNavBar.AddChild(_coreTabBtn);
        _unifiedNavBar.AddChild(_installedTabBtn);
        _unifiedNavBar.AddChild(_refreshBtn);
        root.AddChild(_unifiedNavBar);

        // ---------------------------------------------------------------------
        // TAB 0: CORE & AUTOMATIC RECORD PROVISIONING
        // ---------------------------------------------------------------------
        _coreView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6
        };

        var statusCard = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1D2C"),
                BorderColor = Color.FromHex("#1E4D6B"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        var statusCardBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _dataServerLabel = new ContentLabel
        {
            Name = "NetCoreDataServerLabel",
            Text = "Data Servers: Checking...",
            FontColorOverride = Color.FromHex("#88EECC"),
            HorizontalExpand = true
        };
        _statsSummaryLabel = new ContentLabel
        {
            Name = "NetCoreStatsSummaryLabel",
            Text = "Devices: 0 | Cameras: 0 | Sessions: 0 | Blacklisted: 0 | Packets: 0",
            FontColorOverride = Color.FromHex("#D0EEF8"),
            HorizontalExpand = true
        };
        _lastSyncLabel = new ContentLabel
        {
            Name = "NetCoreLastSyncLabel",
            Text = "Last Sync: --",
            FontColorOverride = Color.FromHex("#99C4D6"),
            HorizontalExpand = true
        };

        var actionRow = new ContentWrapContainer { HorizontalSeparation = 6, VerticalSeparation = 4 };
        _autoSyncToggleBtn = new ContentButton { Name = "NetCoreAutoSyncToggleBtn", Text = "Auto-Provision Joining Crew: ON", MinWidth = 210 };
        _autoSyncToggleBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.ToggleAutoSync));

        _forceSyncBtn = new ContentButton { Name = "NetCoreForceSyncBtn", Text = "Force Resync All Station Records", MinWidth = 210 };
        _forceSyncBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.ForceSyncAllRecords));

        actionRow.AddChild(_autoSyncToggleBtn);
        actionRow.AddChild(_forceSyncBtn);

        statusCardBox.AddChild(_dataServerLabel);
        statusCardBox.AddChild(_statsSummaryLabel);
        statusCardBox.AddChild(_lastSyncLabel);
        statusCardBox.AddChild(actionRow);
        statusCard.AddChild(statusCardBox);
        _coreView.AddChild(statusCard);

        _coreView.AddChild(new ContentLabel
        {
            Text = "NETWORK CORE PROVISIONING & SYNC LOG:",
            FontColorOverride = Color.FromHex("#77B8D4")
        });

        var logScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 160f
        };
        _syncLogsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2
        };
        logScroll.AddChild(_syncLogsContainer);
        _coreView.AddChild(logScroll);
        root.AddChild(_coreView);

        // ---------------------------------------------------------------------
        // TAB 1: MOUNTED CHILD PROGRAM VIEWPORT (Router Admin / Terminal / etc.)
        // ---------------------------------------------------------------------
        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        root.AddChild(_childViewport);

        // ---------------------------------------------------------------------
        // TAB 2: INSTALLED EXECUTABLES
        // ---------------------------------------------------------------------
        _installedView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var appScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _installedContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        appScroll.AddChild(_installedContainer);
        _installedView.AddChild(appScroll);
        root.AddChild(_installedView);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NetworkCoreOsUiState state)
            return;

        _activeChildNet = state.ActiveChildProgram;
        var hw = state.Hardware;

        _headerLabel.Text = $"{state.HostName} — NETWORK CORE OS [{state.DeviceGuid}]";
        _hwLabel.Text = $"CPU: {hw.CpuMhz}MHz | RAM: {hw.RamUsedMb}/{hw.RamTotalMb}MB | HDD: {hw.StorageUsedBytes / 1024}KB | NIC: {(hw.HasNetworkCard ? "ONLINE" : "OFFLINE")}";

        _coreTabBtn.Pressed = state.CurrentTab == 0;
        _installedTabBtn.Pressed = state.CurrentTab == 2;

        _coreView.Visible = state.CurrentTab == 0;
        _childViewport.Visible = state.CurrentTab == 1;
        _installedView.Visible = state.CurrentTab == 2;

        _dataServerLabel.Text = $"Data Servers: {state.DataServerSummary}";
        _dataServerLabel.FontColorOverride = state.DataServerOnline
            ? Color.FromHex("#66EEAA")
            : Color.FromHex("#FFD166");

        _statsSummaryLabel.Text =
            $"Devices: {state.ConnectedDevicesCount} | Wi-Fi Cameras: {state.ConnectedCamerasCount} | Active Sessions: {state.ActiveConnectionsCount} | Blacklisted: {state.BlacklistedCount} | Packets: {state.RecordedPacketsCount}";
        _lastSyncLabel.Text = $"Auto-Synced Joins: {state.AutoSyncCount} | {state.LastSyncSummary}";

        _autoSyncToggleBtn.Text = state.AutoSyncRecords
            ? "Auto-Provision Joining Crew: ON"
            : "Auto-Provision Joining Crew: OFF";
        _autoSyncToggleBtn.Pressed = state.AutoSyncRecords;

        _syncLogsContainer.RemoveAllChildren();
        for (var i = state.SyncLogs.Count - 1; i >= 0; i--)
        {
            _syncLogsContainer.AddChild(new ContentLabel
            {
                Text = state.SyncLogs[i],
                FontColorOverride = Color.FromHex("#CFEAF5"),
                HorizontalExpand = true
            });
        }

        foreach (var old in _inlineRunningTabs)
        {
            if (!old.Disposed && old.Parent == _unifiedNavBar)
                _unifiedNavBar.RemoveChild(old);
        }
        _inlineRunningTabs.Clear();

        foreach (var rp in state.RunningPrograms)
        {
            if (rp.IsBootProgram || rp.RunningEntity is not { } rEnt)
                continue;

            var pair = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 1 };
            var switchBtn = new ContentButton
            {
                Text = rp.Name,
                Icon = _icons.ResolveProgramIcon(rp),
                IconSize = new Vector2(14, 14),
                IconModulate = Color.White,
                Pressed = state.CurrentTab == 1 && state.ActiveChildProgram == rEnt
            };
            switchBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.SwitchProgram, programEntity: rEnt));
            pair.AddChild(switchBtn);

            var closeBtn = new ContentButton { Text = "x", MinWidth = 22 };
            closeBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.CloseProgram, programEntity: rEnt));
            pair.AddChild(closeBtn);
            _inlineRunningTabs.Add(pair);
            _unifiedNavBar.AddChild(pair);
        }

        _installedContainer.RemoveAllChildren();
        foreach (var exec in state.InstalledExecutables)
        {
            if (exec.IsBootProgram)
                continue;

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
            };
            var iconTex = _icons.ResolveProgramIcon(exec);
            if (iconTex != null)
            {
                row.AddChild(new ContentTextureRect
                {
                    Texture = iconTex,
                    DisplaySize = new Vector2(18, 18),
                    VerticalAlignment = VAlignment.Center
                });
            }
            row.AddChild(new ContentLabel
            {
                Text = $"{exec.Name} ({exec.FilePath})",
                HorizontalExpand = true,
                VerticalAlignment = VAlignment.Center
            });

            if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var openBtn = new ContentButton { Text = "Open", MinWidth = 68 };
                openBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.SwitchProgram, programEntity: runEnt));
                row.AddChild(openBtn);
            }
            else
            {
                var runBtn = new ContentButton { Text = "Run .nte", MinWidth = 74 };
                var path = exec.FilePath;
                runBtn.OnPressed += _ => SendProgramMessage(new NetworkCoreOsActionMessage(NetworkCoreOsAction.LaunchProgram, executablePath: path));
                row.AddChild(runBtn);
            }

            _installedContainer.AddChild(row);
        }

        SyncVisibleChildProgram();
    }

    public void AttachChildProgram(EntityUid programUid, ProgramUiControl control)
    {
        if (Disposed || _childViewport.Disposed || control.Disposed)
            return;

        if (_mountedChildren.TryGetValue(programUid, out var existing) && existing != control)
        {
            if (!existing.Disposed && existing.Parent == _childViewport)
                _childViewport.RemoveChild(existing);
        }

        _mountedChildren[programUid] = control;
        if (control.Parent != _childViewport)
        {
            control.Parent?.RemoveChild(control);
            control.HorizontalExpand = true;
            control.VerticalExpand = true;
            _childViewport.AddChild(control);
        }

        SyncVisibleChildProgram();
    }

    public void DetachChildProgram(EntityUid programUid, ProgramUiControl control)
    {
        _mountedChildren.Remove(programUid);
        if (!control.Disposed && !_childViewport.Disposed && control.Parent == _childViewport)
            _childViewport.RemoveChild(control);
    }

    public void DetachAllChildPrograms()
    {
        if (!_childViewport.Disposed)
        {
            foreach (var ctrl in _mountedChildren.Values)
            {
                if (!ctrl.Disposed && ctrl.Parent == _childViewport)
                    _childViewport.RemoveChild(ctrl);
            }
        }
        _mountedChildren.Clear();
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DetachAllChildPrograms();

        base.Dispose(disposing);
    }

    private void SyncVisibleChildProgram()
    {
        var targetUid = _activeChildNet != null && _entMan.TryGetEntity(_activeChildNet.Value, out var u)
            ? u
            : (EntityUid?) null;

        foreach (var (pUid, ctrl) in _mountedChildren)
            ctrl.Visible = targetUid == pUid;
    }
}
