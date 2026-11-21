using System.Collections.Generic;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated handheld Operating System shell for PDAs (/bin/pda_os.nte -> PdaOsProgramBui).
/// Uses wrapping containers and scrollable views throughout so no controls overflow on compact screens.
/// </summary>
public sealed class PdaOsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private readonly IEntityManager _entMan;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _identityLabel;
    private readonly ContentLabel _statusBarLabel;
    private readonly ContentWrapContainer _navBar;
    private readonly ContentButton _activeAppNavBtn;
    private readonly ContentWrapContainer _tabStrip;

    private readonly ContentScrollContainer _homeScroll;
    private readonly ContentLabel _stationStatusLabel;
    private readonly ContentLabel _slotsSummaryLabel;
    private readonly ContentWrapContainer _slotActionsWrap;
    private readonly ContentWrapContainer _utilitiesWrap;
    private readonly ContentWrapContainer _quickAppsWrap;

    private readonly ContentBoxContainer _appsView;
    private readonly ContentBoxContainer _programListContainer;

    private readonly ContentBoxContainer _deviceView;
    private readonly ContentLabel _hwDetailsLabel;
    private readonly ContentBoxContainer _portsContainer;

    private readonly ContentBoxContainer _childViewport;

    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private NetEntity? _activeChildNet;

    public PdaOsUiControl()
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

        // Top Pocket Status Banner
        var topPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1A26"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };

        var topCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };

        _headerLabel = new ContentLabel
        {
            Text = "NTOS POCKET",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };
        _identityLabel = new ContentLabel
        {
            Text = "No ID Card Inserted",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        _statusBarLabel = new ContentLabel
        {
            Text = "Initializing Pocket OS...",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        topCol.AddChild(_headerLabel);
        topCol.AddChild(_identityLabel);
        topCol.AddChild(_statusBarLabel);
        topPanel.AddChild(topCol);
        root.AddChild(topPanel);

        // Wrapped Pocket Navigation Bar
        _navBar = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _navBar.AddChild(MakeNavBtn("Home", 0));
        _navBar.AddChild(MakeNavBtn("Apps", 1));
        _navBar.AddChild(MakeNavBtn("Device", 2));
        _activeAppNavBtn = MakeNavBtn("Active App", 3);
        _activeAppNavBtn.Visible = false;
        _navBar.AddChild(_activeAppNavBtn);
        root.AddChild(_navBar);

        // Wrapped Running App Tabs
        _tabStrip = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_tabStrip);

        // View 0: PDA Pocket Home Dashboard (scrollable)
        _homeScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        var homeContent = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };

        _stationStatusLabel = new ContentLabel
        {
            Text = "Station: Unassigned",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFE066"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        homeContent.AddChild(_stationStatusLabel);

        homeContent.AddChild(new ContentLabel
        {
            Text = "PDA SLOTS & CARTRIDGES:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 2, 0, 2)
        });

        _slotsSummaryLabel = new ContentLabel
        {
            Text = "ID: Empty | Pen: Empty | Disk: Empty",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#D6F5F5"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        homeContent.AddChild(_slotsSummaryLabel);

        _slotActionsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 8)
        };
        homeContent.AddChild(_slotActionsWrap);

        homeContent.AddChild(new ContentLabel
        {
            Text = "POCKET UTILITIES:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 2, 0, 2)
        });

        _utilitiesWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 8)
        };
        homeContent.AddChild(_utilitiesWrap);

        homeContent.AddChild(new ContentLabel
        {
            Text = "QUICK LAUNCH APPS:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 2, 0, 4)
        });

        _quickAppsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 6,
            Margin = new Thickness(0, 0, 0, 6)
        };
        homeContent.AddChild(_quickAppsWrap);

        _homeScroll.AddChild(homeContent);
        root.AddChild(_homeScroll);

        // View 1: Installed .nte Apps Manager
        _appsView = new ContentBoxContainer
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
        _appsView.AddChild(appScroll);
        root.AddChild(_appsView);

        // View 2: Device Diagnostics & Mirrored DeviceLink Ports
        _deviceView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var deviceScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var deviceCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        _hwDetailsLabel = new ContentLabel
        {
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        deviceCol.AddChild(_hwDetailsLabel);
        _portsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        deviceCol.AddChild(_portsContainer);
        deviceScroll.AddChild(deviceCol);
        _deviceView.AddChild(deviceScroll);
        root.AddChild(_deviceView);

        // View 3: Parented Child Program Viewport
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
            MinWidth = 64
        };
        btn.OnPressed += _ => SendProgramMessage(new PdaOsSetViewMessage(viewIdx));
        return btn;
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not PdaOsUiState state)
            return;

        _activeChildNet = state.ActiveChildProgram;
        var hw = state.Hardware;

        var netStr = hw.HasNetworkCard
            ? $"{hw.BandwidthBps / 1024}KB/s{(!string.IsNullOrEmpty(hw.NetworkLabel) ? $" ({hw.NetworkLabel})" : string.Empty)}"
            : "NONE";

        _headerLabel.Text = $"{state.HostTitle} | Pocket OS [{hw.DeviceGuid}]";
        _identityLabel.Text = !string.IsNullOrWhiteSpace(state.OwnerName)
            ? $"ID: {state.OwnerName}{(!string.IsNullOrWhiteSpace(state.OwnerJob) ? $" — {state.OwnerJob}" : string.Empty)}"
            : (state.InsertedIdName != null ? $"ID: {state.InsertedIdName}" : "ID: No ID Card Inserted");
        _statusBarLabel.Text =
            $"CPU: {hw.CpuMhz}MHz | RAM: {hw.RamUsedMb}/{hw.RamTotalMb}MB | HDD: {hw.StorageUsedBytes / 1024}/{hw.StorageTotalBytes / 1024}KB | NET: {netStr}";

        _activeAppNavBtn.Visible = state.ActiveChildProgram != null;

        _homeScroll.Visible = state.CurrentView == 0;
        _appsView.Visible = state.CurrentView == 1;
        _deviceView.Visible = state.CurrentView == 2;
        _childViewport.Visible = state.CurrentView == 3;

        RebuildHomeDashboard(state);
        RebuildTabStrip(state);
        RebuildProgramList(state);
        RebuildDeviceView(state);
        SyncVisibleChildProgram();
    }

    private void RebuildHomeDashboard(PdaOsUiState state)
    {
        var stationPart = string.IsNullOrWhiteSpace(state.StationName) ? "Unassigned" : state.StationName;
        var alertPart = string.IsNullOrWhiteSpace(state.StationAlertLevel) ? "Standard" : state.StationAlertLevel.ToUpperInvariant();
        _stationStatusLabel.Text = $"Station: {stationPart} | Alert Level: {alertPart}";
        _stationStatusLabel.FontColorOverride = state.StationAlertColor == Color.White
            ? Color.FromHex("#FFE066")
            : state.StationAlertColor;

        _slotsSummaryLabel.Text =
            $"ID Slot: {state.InsertedIdName ?? "Empty"} | Pen: {state.InsertedPenName ?? "Empty"} | Disk: {state.InsertedDiskName ?? "Empty"}" +
            (state.InsertedPaiName != null ? $" | pAI: {state.InsertedPaiName}" : string.Empty);

        _slotActionsWrap.RemoveAllChildren();
        AddHardwareBtn(_slotActionsWrap, "Eject ID", "eject_id", disabled: state.InsertedIdName == null);
        AddHardwareBtn(_slotActionsWrap, "Eject Pen", "eject_pen", disabled: state.InsertedPenName == null);
        AddHardwareBtn(_slotActionsWrap, "Eject Disk", "eject_disk", disabled: state.InsertedDiskName == null);
        if (state.InsertedPaiName != null)
            AddHardwareBtn(_slotActionsWrap, "Eject pAI", "eject_pai", disabled: false);

        _utilitiesWrap.RemoveAllChildren();
        AddHardwareBtn(_utilitiesWrap, state.FlashlightOn ? "Flashlight: ON" : "Flashlight: OFF", "toggle_flashlight");
        if (state.HasRinger)
            AddHardwareBtn(_utilitiesWrap, "Ringtone", "show_ringtone");
        if (state.HasInstrument)
            AddHardwareBtn(_utilitiesWrap, "Music", "show_music");
        if (state.HasUplinkUnlocked)
        {
            AddHardwareBtn(_utilitiesWrap, "Uplink", "show_uplink");
            AddHardwareBtn(_utilitiesWrap, "Lock Uplink", "lock_uplink");
        }

        _quickAppsWrap.RemoveAllChildren();
        foreach (var exec in state.InstalledExecutables)
        {
            if (exec.IsBootProgram)
                continue;

            var tile = new ContentButton
            {
                Text = exec.IsRunning ? $"{exec.Name} *" : exec.Name,
                MinWidth = 96
            };

            if (exec.IsRunning && exec.RunningEntity is { } runEnt)
                tile.OnPressed += _ => SendProgramMessage(new PdaOsSwitchProgramMessage(runEnt));
            else
            {
                var path = exec.FilePath;
                tile.OnPressed += _ => SendProgramMessage(new PdaOsLaunchProgramMessage(path));
            }

            _quickAppsWrap.AddChild(tile);
        }
    }

    private void AddHardwareBtn(ContentWrapContainer parent, string text, string action, bool disabled = false)
    {
        var btn = new ContentButton
        {
            Text = text,
            Disabled = disabled
        };
        btn.OnPressed += _ => SendProgramMessage(new PdaOsHardwareActionMessage(action));
        parent.AddChild(btn);
    }

    private void RebuildTabStrip(PdaOsUiState state)
    {
        _tabStrip.RemoveAllChildren();
        var hasTabs = false;

        foreach (var prog in state.RunningPrograms)
        {
            if (prog.IsBootProgram || prog.RunningEntity is not { } rEnt)
                continue;

            hasTabs = true;
            var tabPair = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal
            };

            var tabBtn = new ContentButton
            {
                Text = prog.Name + (prog.RunningEntity == state.ActiveChildProgram && state.CurrentView == 3 ? " *" : "")
            };
            tabBtn.OnPressed += _ => SendProgramMessage(new PdaOsSwitchProgramMessage(rEnt));
            tabPair.AddChild(tabBtn);

            var closeBtn = new ContentButton
            {
                Text = "x",
                MinWidth = 24,
                Margin = new Thickness(2, 0, 0, 0)
            };
            closeBtn.OnPressed += _ => SendProgramMessage(new PdaOsCloseProgramMessage(rEnt));
            tabPair.AddChild(closeBtn);

            _tabStrip.AddChild(tabPair);
        }

        _tabStrip.Visible = hasTabs;
    }

    private void RebuildProgramList(PdaOsUiState state)
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

            row.AddChild(new ContentLabel
            {
                Text = $"{exec.Name} [{exec.FilePath}] ({exec.RamUsageMb} MB)",
                HorizontalExpand = true,
                FontColorOverride = exec.IsBootProgram ? Color.FromHex("#88FFAA") : Color.FromHex("#E0F7FA")
            });

            if (exec.IsBootProgram)
            {
                row.AddChild(new ContentLabel
                {
                    Text = "[PDA OS]",
                    WordWrap = false,
                    FontColorOverride = Color.FromHex("#00D4FF"),
                    Margin = new Thickness(4, 0, 0, 0)
                });
            }
            else if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var focusBtn = new ContentButton { Text = "Focus", Margin = new Thickness(2, 0), MinWidth = 58 };
                focusBtn.OnPressed += _ => SendProgramMessage(new PdaOsSwitchProgramMessage(runEnt));
                row.AddChild(focusBtn);

                var stopBtn = new ContentButton { Text = "Stop", Margin = new Thickness(2, 0), MinWidth = 52 };
                stopBtn.OnPressed += _ => SendProgramMessage(new PdaOsCloseProgramMessage(runEnt));
                row.AddChild(stopBtn);
            }
            else
            {
                var path = exec.FilePath;
                var runBtn = new ContentButton { Text = "Run .nte", Margin = new Thickness(2, 0), MinWidth = 72 };
                runBtn.OnPressed += _ => SendProgramMessage(new PdaOsLaunchProgramMessage(path));
                row.AddChild(runBtn);
            }

            _programListContainer.AddChild(row);
        }
    }

    private void RebuildDeviceView(PdaOsUiState state)
    {
        var hw = state.Hardware;
        _hwDetailsLabel.Text =
            $"Device: {hw.HostName} | GUID: {hw.DeviceGuid}\n" +
            $"CPU: {hw.CpuMhz} MHz (max {hw.MaxPrograms} apps) | RAM: {hw.RamUsedMb}/{hw.RamTotalMb} MB\n" +
            $"HDD: {hw.StorageUsedBytes / 1024}/{hw.StorageTotalBytes / 1024} KB | DataPort: {(hw.HasDataPort ? "PRESENT" : "NONE")}\n" +
            $"RemoteCtrl: {hw.AllowRemoteControl} | DeviceLink: {hw.AllowDeviceLink} | Flashlight: {(state.FlashlightOn ? "ON" : "OFF")}";

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

            var portId = port.PortId;
            var isHost = port.IsHostPort;
            var trigBtn = new ContentButton { Text = "Pulse", MinWidth = 55, Margin = new Thickness(4, 0, 0, 0) };
            trigBtn.OnPressed += _ => SendProgramMessage(new PdaOsInvokeDevicePortMessage(portId, isHost, "status"));
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
