using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Primary desktop/PDA Operating System shell UI.
/// Controls the entire display surface and parents child program BUI controls
/// inside its workspace viewport (<see cref="_childViewport"/>).
/// </summary>
public sealed class OsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private readonly IEntityManager _entMan;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusBarLabel;
    private readonly ContentBoxContainer _tabStrip;
    private readonly ContentBoxContainer _homeView;
    private readonly ContentBoxContainer _programsView;
    private readonly ContentBoxContainer _hardwareView;
    private readonly ContentBoxContainer _childViewport;
    private readonly ContentBoxContainer _programListContainer;
    private readonly ContentBoxContainer _portsContainer;
    private readonly ContentLabel _hwDetailsLabel;

    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private OsUiState? _lastState;
    private NetEntity? _activeChildNet;

    public OsUiControl()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6)
        };

        // Top OS Header + Navigation Bar
        var navRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _headerLabel = new ContentLabel
        {
            Text = "NTOS 1.4",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };
        navRow.AddChild(_headerLabel);

        navRow.AddChild(MakeNavBtn("Home", 0));
        navRow.AddChild(MakeNavBtn("Apps", 1));
        navRow.AddChild(MakeNavBtn("HW / Links", 2));
        root.AddChild(navRow);

        // Running Child Program Tabs
        _tabStrip = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_tabStrip);

        // Status / Metrics strip
        _statusBarLabel = new ContentLabel
        {
            Text = "Initializing NTOS...",
            FontColorOverride = Color.FromHex("#88CCCC"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_statusBarLabel);

        // View 0: Home & Quick Hardware Actions
        _homeView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        BuildHomeView();
        root.AddChild(_homeView);

        // View 1: Installed .nte Executables & Running Tasks
        _programsView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var appScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _programListContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        appScroll.AddChild(_programListContainer);
        _programsView.AddChild(appScroll);
        root.AddChild(_programsView);

        // View 2: Hardware & Mirrored DeviceLink Ports
        _hardwareView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        _hwDetailsLabel = new ContentLabel { FontColorOverride = Color.FromHex("#E0F7FA"), Margin = new Thickness(0, 0, 0, 6) };
        _hardwareView.AddChild(_hwDetailsLabel);
        var portScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _portsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        portScroll.AddChild(_portsContainer);
        _hardwareView.AddChild(portScroll);
        root.AddChild(_hardwareView);

        // View 3: Parented Child Program Viewport (OS owns & parents target program BUI here!)
        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        root.AddChild(_childViewport);

        AddChild(root);
    }

    private ContentButton MakeNavBtn(string text, int viewIdx)
    {
        var btn = new ContentButton
        {
            Text = text,
            Margin = new Thickness(2, 0),
            MinWidth = 65
        };
        btn.OnPressed += _ => SendProgramMessage(new OsSetViewMessage(viewIdx));
        return btn;
    }

    private void BuildHomeView()
    {
        var actionsRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 4, 0, 8)
        };

        void AddHwBtn(string label, string action)
        {
            var btn = new ContentButton
            {
                Text = label,
                Margin = new Thickness(2, 0)
            };
            btn.OnPressed += _ => SendProgramMessage(new OsHardwareActionMessage(action));
            actionsRow.AddChild(btn);
        }

        AddHwBtn("Flashlight", "toggle_flashlight");
        AddHwBtn("Ringtone", "show_ringtone");
        AddHwBtn("Music", "show_music");
        AddHwBtn("Uplink", "show_uplink");
        AddHwBtn("Eject ID", "eject_id");
        AddHwBtn("Eject Pen", "eject_pen");
        AddHwBtn("Eject Disk", "eject_disk");

        _homeView.AddChild(new ContentLabel
        {
            Text = "HOST DEVICE CONTROLS (via DeviceLink):",
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 4)
        });
        _homeView.AddChild(actionsRow);

        var openAppsBtn = new ContentButton
        {
            Text = "Open Installed Programs (.nte)",
            HorizontalAlignment = HAlignment.Left,
            MinWidth = 240
        };
        openAppsBtn.OnPressed += _ => SendProgramMessage(new OsSetViewMessage(1));
        _homeView.AddChild(openAppsBtn);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not OsUiState state)
            return;

        _lastState = state;
        _activeChildNet = state.ActiveChildProgram;

        _headerLabel.Text = $"{state.HostTitle} | {state.OwnerName ?? "Guest"} {(state.OwnerJob != null ? $"({state.OwnerJob})" : "")}";
        var hw = state.Hardware;
        var netStr = hw.HasNetworkCard
            ? $"{hw.BandwidthBps / 1024}KB/s{(!string.IsNullOrEmpty(hw.NetworkLabel) ? $" ({hw.NetworkLabel})" : string.Empty)}"
            : "NONE";
        _statusBarLabel.Text =
            $"GUID: {hw.DeviceGuid} | CPU: {hw.CpuMhz}MHz | RAM: {hw.RamUsedMb}/{hw.RamTotalMb}MB | HDD: {hw.StorageUsedBytes / 1024}/{hw.StorageTotalBytes / 1024}KB | NIC: {netStr}";

        _homeView.Visible = state.CurrentView == 0;
        _programsView.Visible = state.CurrentView == 1;
        _hardwareView.Visible = state.CurrentView == 2;
        _childViewport.Visible = state.CurrentView == 3;

        RebuildTabStrip(state);
        RebuildProgramList(state);
        RebuildHardwareAndPorts(state);
        SyncVisibleChildProgram();
    }

    private void RebuildTabStrip(OsUiState state)
    {
        _tabStrip.RemoveAllChildren();
        foreach (var prog in state.RunningPrograms)
        {
            if (prog.IsBootProgram || prog.RunningEntity is not { } rEnt)
                continue;

            var tabBtn = new ContentButton
            {
                Text = prog.Name + (prog.RunningEntity == state.ActiveChildProgram && state.CurrentView == 3 ? " *" : ""),
                Margin = new Thickness(2, 0)
            };
            tabBtn.OnPressed += _ => SendProgramMessage(new OsSwitchProgramMessage(rEnt));
            _tabStrip.AddChild(tabBtn);

            var closeBtn = new ContentButton
            {
                Text = "x",
                Margin = new Thickness(0, 0, 4, 0),
                MinWidth = 22
            };
            closeBtn.OnPressed += _ => SendProgramMessage(new OsCloseProgramMessage(rEnt));
            _tabStrip.AddChild(closeBtn);
        }
    }

    private void RebuildProgramList(OsUiState state)
    {
        _programListContainer.RemoveAllChildren();
        foreach (var exec in state.InstalledExecutables)
        {
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 2)
            };

            var info = new ContentLabel
            {
                Text = $"{exec.Name}  [{exec.FilePath}]  ({exec.RamUsageMb} MB)",
                HorizontalExpand = true,
                FontColorOverride = exec.IsBootProgram ? Color.FromHex("#88FFAA") : Color.FromHex("#E0F7FA")
            };
            row.AddChild(info);

            if (exec.IsBootProgram)
            {
                row.AddChild(new ContentLabel { Text = "[BOOT OS]", FontColorOverride = Color.FromHex("#00D4FF") });
            }
            else if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var focusBtn = new ContentButton { Text = "Focus", Margin = new Thickness(2, 0), MinWidth = 60 };
                focusBtn.OnPressed += _ => SendProgramMessage(new OsSwitchProgramMessage(runEnt));
                row.AddChild(focusBtn);

                var stopBtn = new ContentButton { Text = "Stop", Margin = new Thickness(2, 0), MinWidth = 55 };
                stopBtn.OnPressed += _ => SendProgramMessage(new OsCloseProgramMessage(runEnt));
                row.AddChild(stopBtn);
            }
            else
            {
                var runBtn = new ContentButton { Text = "Run .nte", Margin = new Thickness(2, 0), MinWidth = 75 };
                runBtn.OnPressed += _ => SendProgramMessage(new OsLaunchProgramMessage(exec.FilePath));
                row.AddChild(runBtn);
            }

            _programListContainer.AddChild(row);
        }
    }

    private void RebuildHardwareAndPorts(OsUiState state)
    {
        var hw = state.Hardware;
        _hwDetailsLabel.Text =
            $"Host: {hw.HostName} | DataPort: {(hw.HasDataPort ? "PRESENT" : "NONE")} | RemoteCtrl: {hw.AllowRemoteControl} | DeviceLink: {hw.AllowDeviceLink}\n" +
            $"Disk: {state.InsertedDiskName ?? "None"} | Flashlight: {(state.FlashlightOn ? "ON" : "OFF")}";

        _portsContainer.RemoveAllChildren();
        foreach (var port in state.DevicePorts)
        {
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 2)
            };
            var scope = port.IsHostPort ? "HOST" : "COMP";
            var dir = port.IsSource ? "OUT" : "IN ";
            var links = port.LinkedTargets.Count > 0 ? string.Join(", ", port.LinkedTargets) : "(none)";

            row.AddChild(new ContentLabel
            {
                Text = $"[{scope} {dir}] {port.PortId} ({port.PortName}) -> {links}",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#CCCCCC")
            });

            var trigBtn = new ContentButton { Text = "Pulse", MinWidth = 55 };
            trigBtn.OnPressed += _ => SendProgramMessage(new OsInvokeDevicePortMessage(port.PortId, port.IsHostPort, "status"));
            row.AddChild(trigBtn);

            _portsContainer.AddChild(row);
        }
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

/// <summary>
/// Boot OS shell for embedded wall/machine controllers (APC, AirAlarm, Autolathe, Router).
/// Parents target subsystem programs cleanly inside its viewport and exposes host DeviceLink ports.
/// </summary>
public sealed class EmbeddedOsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private readonly IEntityManager _entMan;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentButton _appViewBtn;
    private readonly ContentButton _listViewBtn;
    private readonly ContentButton _linkViewBtn;
    private readonly ContentBoxContainer _tabRow;
    private readonly ContentBoxContainer _childViewport;
    private readonly ContentBoxContainer _appsView;
    private readonly ContentBoxContainer _portsView;
    private readonly ContentBoxContainer _appsContainer;
    private readonly ContentBoxContainer _portsContainer;
    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private NetEntity? _activeChildNet;

    public EmbeddedOsUiControl()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6)
        };

        var topRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _headerLabel = new ContentLabel
        {
            Text = "EMBEDDED CONTROLLER OS",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFA726")
        };
        topRow.AddChild(_headerLabel);

        _appViewBtn = new ContentButton { Text = "Controller", Margin = new Thickness(2, 0), MinWidth = 75 };
        _appViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(0));
        _listViewBtn = new ContentButton { Text = "Programs (.nte)", Margin = new Thickness(2, 0), MinWidth = 100 };
        _listViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(1));
        _linkViewBtn = new ContentButton { Text = "DeviceLinks", Margin = new Thickness(2, 0), MinWidth = 85 };
        _linkViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(2));

        topRow.AddChild(_appViewBtn);
        topRow.AddChild(_listViewBtn);
        topRow.AddChild(_linkViewBtn);
        root.AddChild(topRow);

        _tabRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_tabRow);

        _statusLabel = new ContentLabel
        {
            Text = "Initializing...",
            FontColorOverride = Color.FromHex("#88AAAA"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_statusLabel);

        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        root.AddChild(_childViewport);

        _appsView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var appScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _appsContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        appScroll.AddChild(_appsContainer);
        _appsView.AddChild(appScroll);
        root.AddChild(_appsView);

        _portsView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var pScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _portsContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        pScroll.AddChild(_portsContainer);
        _portsView.AddChild(pScroll);
        root.AddChild(_portsView);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not EmbeddedOsUiState state)
            return;

        _activeChildNet = state.ActiveChildProgram;
        var hw = state.Hardware;

        var nicText = hw.HasNetworkCard
            ? (!string.IsNullOrEmpty(hw.NetworkLabel) ? $"UP ({hw.NetworkLabel})" : "UP")
            : "NONE";
        var lockSuffix = state.HasLock
            ? (state.IsHostLocked ? " | [LOCKED]" : " | [UNLOCKED]")
            : string.Empty;
        _headerLabel.Text = $"{state.HostName} | [{state.DeviceGuid}]{lockSuffix}";
        _statusLabel.Text = $"CPU: {hw.CpuMhz}MHz | RAM: {hw.RamUsedMb}/{hw.RamTotalMb}MB | HDD: {hw.StorageUsedBytes / 1024}KB | NIC: {nicText} | DataPort: {(hw.HasDataPort ? "OK" : "NO")}";

        _appViewBtn.Disabled = state.IsHostLocked;
        _listViewBtn.Disabled = state.IsHostLocked;
        _linkViewBtn.Disabled = state.IsHostLocked;

        _childViewport.Visible = state.CurrentView == 0;
        _appsView.Visible = state.CurrentView == 1;
        _portsView.Visible = state.CurrentView == 2;

        _tabRow.RemoveAllChildren();
        foreach (var rp in state.RunningPrograms)
        {
            if (rp.IsBootProgram || rp.RunningEntity is not { } ent)
                continue;

            var btn = new ContentButton { Text = rp.Name, Margin = new Thickness(2, 0), Disabled = state.IsHostLocked };
            btn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSwitchProgramMessage(ent));
            _tabRow.AddChild(btn);

            var closeBtn = new ContentButton { Text = "x", Margin = new Thickness(0, 0, 4, 0), MinWidth = 22, Disabled = state.IsHostLocked };
            closeBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsCloseProgramMessage(ent));
            _tabRow.AddChild(closeBtn);
        }

        _appsContainer.RemoveAllChildren();
        foreach (var exec in state.InstalledExecutables)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            row.AddChild(new ContentLabel { Text = $"{exec.Name} ({exec.FilePath})", HorizontalExpand = true });

            if (!exec.IsBootProgram)
            {
                if (exec.IsRunning && exec.RunningEntity is { } runEnt)
                {
                    var sw = new ContentButton { Text = "Open", MinWidth = 60, Disabled = state.IsHostLocked };
                    sw.OnPressed += _ => SendProgramMessage(new EmbeddedOsSwitchProgramMessage(runEnt));
                    row.AddChild(sw);
                }
                else
                {
                    var run = new ContentButton { Text = "Run .nte", MinWidth = 75, Disabled = state.IsHostLocked };
                    run.OnPressed += _ => SendProgramMessage(new EmbeddedOsLaunchProgramMessage(exec.FilePath));
                    row.AddChild(run);
                }
            }
            _appsContainer.AddChild(row);
        }

        _portsContainer.RemoveAllChildren();
        foreach (var port in state.MirroredPorts)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            row.AddChild(new ContentLabel
            {
                Text = $"[{(port.IsHostPort ? "HOST" : "COMP")} {(port.IsSource ? "OUT" : "IN ")}] {port.PortId} -> {(port.LinkedTargets.Count > 0 ? string.Join(", ", port.LinkedTargets) : "(none)")}",
                HorizontalExpand = true
            });
            var pulse = new ContentButton { Text = "Status", MinWidth = 60, Disabled = state.IsHostLocked };
            pulse.OnPressed += _ => SendProgramMessage(new EmbeddedOsInvokePortMessage(port.PortId, port.IsHostPort, "status"));
            row.AddChild(pulse);
            _portsContainer.AddChild(row);
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
