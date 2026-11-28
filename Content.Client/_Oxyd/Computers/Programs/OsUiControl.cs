using System.Collections.Generic;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.GameTicking;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Clock;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Refined Desktop / Workstation Operating System shell UI (<c>/bin/os.nte</c>).
/// Features a single unified top tab bar (Home, System, plus inline iconified running apps),
/// a merged Desktop &amp; icon-tile App Launcher, and keeps hardware diagnostics &amp; DeviceLink ports
/// neatly tucked inside the System tab.
/// </summary>
public sealed class OsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private const string IconHome = "/Textures/Interface/home.png";
    private const string IconSystem = "/Textures/Interface/VerbIcons/settings.svg.192dpi.png";
    private const string IconClock = "/Textures/Interface/VerbIcons/clock.svg.192dpi.png";
    private const string IconEject = "/Textures/Interface/VerbIcons/eject.svg.192dpi.png";

    private readonly IEntityManager _entMan;
    private readonly IGameTiming _timing;
    private readonly ProgramIconResolver _icons;

    private readonly ContentWrapContainer _unifiedTabBar;
    private readonly ContentButton _homeTabBtn;
    private readonly ContentButton _systemTabBtn;
    private readonly List<Control> _runningAppTabs = new();
    private readonly ContentLabel _clockBadgeLabel;
    private readonly ContentButton _topEjectDiskBtn;
    private readonly ContentButton _osAuthLogoutBtn;
    private bool _hasOsAuth;

    private readonly ContentScrollContainer _desktopScroll;
    private readonly ContentLabel _desktopTitleLabel;
    private readonly ContentLabel _desktopMetaLabel;
    private readonly ContentWrapContainer _desktopAppsGrid;

    private readonly ContentBoxContainer _systemView;
    private readonly ContentLabel _hwDetailsLabel;
    private readonly ContentWrapContainer _slotActionsWrap;
    private readonly ContentLabel _componentsHeaderLabel;
    private readonly ContentBoxContainer _componentsContainer;
    private readonly ContentBoxContainer _portsContainer;

    private readonly ContentBoxContainer _childViewport;

    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private OsUiState? _lastState;
    private NetEntity? _activeChildNet;
    private int _lastRoundSeconds = -1;

    public OsUiControl()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _timing = IoCManager.Resolve<IGameTiming>();
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
            SeparationOverride = 6,
            RectClipContent = true
        };

        // ---------------------------------------------------------------------
        // SINGLE UNIFIED TOP BAR (Home + System + Inline Running App Tabs + Clock)
        // ---------------------------------------------------------------------
        var topBarPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            RectClipContent = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1722"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };

        var topBarRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _unifiedTabBar = new ContentWrapContainer
        {
            Name = "WorkstationUnifiedTabBar",
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };

        _homeTabBtn = new ContentButton
        {
            Name = "TabHomeButton",
            Text = "Home",
            Icon = _icons.TryLoadTexture(IconHome),
            IconSize = new Vector2(15, 15),
            Pressed = true
        };
        _homeTabBtn.OnPressed += _ => SendProgramMessage(new OsSetViewMessage(0));

        _systemTabBtn = new ContentButton
        {
            Name = "TabSystemButton",
            Text = "System",
            Icon = _icons.TryLoadTexture(IconSystem),
            IconSize = new Vector2(15, 15)
        };
        _systemTabBtn.OnPressed += _ => SendProgramMessage(new OsSetViewMessage(2));

        _unifiedTabBar.AddChild(_homeTabBtn);
        _unifiedTabBar.AddChild(_systemTabBtn);

        var rightBar = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 6
        };

        _topEjectDiskBtn = new ContentButton
        {
            Name = "TopEjectDiskButton",
            Icon = _icons.TryLoadTexture(IconEject),
            IconSize = new Vector2(15, 15),
            Text = string.Empty,
            ToolTip = "Eject Data Disk",
            Visible = false
        };
        _topEjectDiskBtn.OnPressed += _ => SendProgramMessage(new OsHardwareActionMessage("eject_disk"));
        rightBar.AddChild(_topEjectDiskBtn);

        _osAuthLogoutBtn = new ContentButton
        {
            Name = "OsAuthLogoutButton",
            Text = "Swipe ID",
            ToolTip = "Swipe ID card to authenticate OS session",
            Visible = true,
            VerticalAlignment = VAlignment.Center
        };
        _osAuthLogoutBtn.OnPressed += _ =>
        {
            if (_hasOsAuth)
                SendProgramMessage(new OsLogoutAuthMessage());
            else
                SendProgramMessage(new OsSwipeAuthMessage());
        };
        rightBar.AddChild(_osAuthLogoutBtn);

        var clockBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4
        };
        var clockTex = _icons.TryLoadTexture(IconClock);
        if (clockTex != null)
        {
            clockBox.AddChild(new ContentTextureRect
            {
                Texture = clockTex,
                DisplaySize = new Vector2(13, 13),
                Modulate = Color.FromHex("#00D4FF"),
                VerticalAlignment = VAlignment.Center
            });
        }
        _clockBadgeLabel = new ContentLabel
        {
            Name = "WorkstationClockLabel",
            Text = "00:00:00",
            WordWrap = false,
            FontColorOverride = Color.FromHex("#88DCE8"),
            VerticalAlignment = VAlignment.Center
        };
        clockBox.AddChild(_clockBadgeLabel);
        rightBar.AddChild(clockBox);

        var shutDownBtn = new ContentButton
        {
            Name = "OsShutDownButton",
            Text = "Shut Down",
            SetWidth = 100,
            VerticalAlignment = VAlignment.Center
        };
        shutDownBtn.OnPressed += _ => SendProgramMessage(new OsShutDownMessage());
        rightBar.AddChild(shutDownBtn);

        topBarRow.AddChild(_unifiedTabBar);
        topBarRow.AddChild(rightBar);
        topBarPanel.AddChild(topBarRow);
        root.AddChild(topBarPanel);

        // ---------------------------------------------------------------------
        // VIEW 0 (and legacy View 1): MERGED DESKTOP & ICON APP LAUNCHER
        // ---------------------------------------------------------------------
        _desktopScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        var desktopCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 8
        };

        var bannerPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1B28"),
                BorderColor = Color.FromHex("#1D4E6C"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };
        var bannerCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        _desktopTitleLabel = new ContentLabel
        {
            Name = "WorkstationBannerTitle",
            Text = "NTOS WORKSTATION",
            FontColorOverride = Color.FromHex("#00E5FF"),
            HorizontalExpand = true
        };
        _desktopMetaLabel = new ContentLabel
        {
            Name = "WorkstationBannerMeta",
            Text = "Initializing terminal...",
            FontColorOverride = Color.FromHex("#9CC7D8"),
            HorizontalExpand = true
        };
        bannerCol.AddChild(_desktopTitleLabel);
        bannerCol.AddChild(_desktopMetaLabel);
        bannerPanel.AddChild(bannerCol);
        desktopCol.AddChild(bannerPanel);

        desktopCol.AddChild(new ContentLabel
        {
            Text = "APPLICATIONS",
            FontColorOverride = Color.FromHex("#79C4DE"),
            HorizontalExpand = true
        });

        _desktopAppsGrid = new ContentWrapContainer
        {
            Name = "DesktopAppsGrid",
            HorizontalExpand = true,
            HorizontalSeparation = 8,
            VerticalSeparation = 8
        };
        desktopCol.AddChild(_desktopAppsGrid);

        _desktopScroll.AddChild(desktopCol);
        root.AddChild(_desktopScroll);

        // ---------------------------------------------------------------------
        // VIEW 2: SYSTEM HARDWARE DIAGNOSTICS, SLOTS & DEVICELINK PORTS
        // ---------------------------------------------------------------------
        _systemView = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        var sysScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var sysInner = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 8
        };

        var hwCard = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1824"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };
        var hwCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        hwCol.AddChild(new ContentLabel
        {
            Text = "HARDWARE & STORAGE DIAGNOSTICS",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        });
        _hwDetailsLabel = new ContentLabel
        {
            Name = "WorkstationHardwareDetailsLabel",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA")
        };
        hwCol.AddChild(_hwDetailsLabel);

        _slotActionsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        hwCol.AddChild(_slotActionsWrap);
        hwCard.AddChild(hwCol);
        sysInner.AddChild(hwCard);

        var compCard = new ContentPanelContainer
        {
            Name = "SystemComponentsCard",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1824"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };
        var compCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _componentsHeaderLabel = new ContentLabel
        {
            Name = "SystemInstalledComponentsHeader",
            Text = "INSTALLED COMPONENTS (0)",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        };
        compCol.AddChild(_componentsHeaderLabel);
        _componentsContainer = new ContentBoxContainer
        {
            Name = "SystemInstalledComponentsContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        compCol.AddChild(_componentsContainer);
        compCard.AddChild(compCol);
        sysInner.AddChild(compCard);

        sysInner.AddChild(new ContentLabel
        {
            Text = "DEVICE LINK PORTS",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        });
        _portsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        sysInner.AddChild(_portsContainer);

        sysScroll.AddChild(sysInner);
        _systemView.AddChild(sysScroll);
        root.AddChild(_systemView);

        // ---------------------------------------------------------------------
        // VIEW 3: MOUNTED CHILD PROGRAM VIEWPORT
        // ---------------------------------------------------------------------
        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            Visible = false
        };
        root.AddChild(_childViewport);

        AddChild(root);
        UpdateClock(force: true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateClock(force: false);
    }

    private void UpdateClock(bool force)
    {
        var stationTime = TimeSpan.Zero;
        if (_entMan.TrySystem<SharedClockSystem>(out var clock))
        {
            stationTime = clock.GetStationTime();
        }
        else if (_entMan.TrySystem<ClientGameTicker>(out var ticker))
        {
            stationTime = _timing.CurTime - ticker.RoundStartTimeSpan;
        }

        if (stationTime < TimeSpan.Zero)
            stationTime = TimeSpan.Zero;

        var totalSeconds = (int) stationTime.TotalSeconds;
        if (!force && totalSeconds == _lastRoundSeconds)
            return;

        _lastRoundSeconds = totalSeconds;
        _clockBadgeLabel.Text = $"{stationTime.Hours:D2}:{stationTime.Minutes:D2}:{stationTime.Seconds:D2}";
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not OsUiState state)
            return;

        _lastState = state;
        _activeChildNet = state.ActiveChildProgram;

        // Treat legacy view 1 (separate Apps) identically to view 0 (merged Desktop + Apps).
        var activeView = state.CurrentView == 1 ? 0 : state.CurrentView;
        if (activeView == 3 && state.ActiveChildProgram == null)
            activeView = 0;

        _desktopScroll.Visible = activeView == 0;
        _systemView.Visible = activeView == 2;
        _childViewport.Visible = activeView == 3;

        _topEjectDiskBtn.Visible = !string.IsNullOrWhiteSpace(state.InsertedDiskName);
        _hasOsAuth = !string.IsNullOrWhiteSpace(state.OsAuthLabel);
        if (_hasOsAuth)
        {
            _osAuthLogoutBtn.Visible = true;
            _osAuthLogoutBtn.Text = $"Logout: {state.OsAuthLabel}";
            _osAuthLogoutBtn.Disabled = false;
            _osAuthLogoutBtn.ToolTip = "Clear active OS user authentication session";
        }
        else if (state.Hardware.HasInsertedIdCard)
        {
            _osAuthLogoutBtn.Visible = false;
        }
        else
        {
            _osAuthLogoutBtn.Visible = true;
            _osAuthLogoutBtn.Text = "Swipe ID";
            _osAuthLogoutBtn.Disabled = !state.Hardware.HasIdSlot;
            _osAuthLogoutBtn.ToolTip = state.Hardware.HasIdSlot
                ? "Swipe ID card to authenticate OS session"
                : "No ID card reader installed";
        }

        var hw = state.Hardware;
        var ownerPart = !string.IsNullOrWhiteSpace(state.OwnerName)
            ? $"  •  User: {state.OwnerName}{(!string.IsNullOrWhiteSpace(state.OwnerJob) ? $" ({state.OwnerJob})" : string.Empty)}"
            : string.Empty;
        _desktopTitleLabel.Text = $"{state.HostTitle}{state.HostSubtitle}{ownerPart}";

        var netText = hw.HasNetworkCard
            ? (!string.IsNullOrEmpty(hw.NetworkLabel) ? $"Online ({hw.NetworkLabel})" : "Online")
            : "Offline (No NIC)";
        var diskPart = !string.IsNullOrWhiteSpace(state.InsertedDiskName)
            ? $"  •  Disk: {state.InsertedDiskName}"
            : string.Empty;
        _desktopMetaLabel.Text = $"GUID: {hw.DeviceGuid}  •  Network: {netText}{diskPart}";

        RebuildUnifiedTabBar(state, activeView);
        RebuildDesktopAppGrid(state);
        RebuildSystemView(state);
        SyncVisibleChildProgram();
    }

    private void RebuildUnifiedTabBar(OsUiState state, int activeView)
    {
        _homeTabBtn.Pressed = activeView == 0;
        _systemTabBtn.Pressed = activeView == 2;

        foreach (var oldTab in _runningAppTabs)
        {
            if (!oldTab.Disposed && oldTab.Parent == _unifiedTabBar)
                _unifiedTabBar.RemoveChild(oldTab);
        }
        _runningAppTabs.Clear();

        foreach (var prog in state.RunningPrograms)
        {
            if (prog.IsBootProgram || prog.RunningEntity is not { } rEnt)
                continue;

            var isFocused = activeView == 3 && prog.RunningEntity == state.ActiveChildProgram;
            var tabPill = CreateRunningAppTabPill(
                prog.Name,
                _icons.ResolveProgramIcon(prog),
                new Vector2(15, 15),
                isFocused,
                disabled: false,
                onSelect: () => SendProgramMessage(new OsSwitchProgramMessage(rEnt)),
                onClose: () => SendProgramMessage(new OsCloseProgramMessage(rEnt)));

            _runningAppTabs.Add(tabPill);
            _unifiedTabBar.AddChild(tabPill);
        }
    }

    internal static ContentPanelContainer CreateRunningAppTabPill(
        string name,
        Texture? icon,
        Vector2 iconSize,
        bool isFocused,
        bool disabled,
        Action onSelect,
        Action? onClose = null)
    {
        var pill = new ContentPanelContainer
        {
            Name = "OsRunningAppTabPill",
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = disabled
                    ? Color.FromHex("#1A1D24")
                    : isFocused
                        ? Color.FromHex("#00D4FF44")
                        : Color.FromHex("#151D28"),
                BorderColor = disabled
                    ? Color.FromHex("#2C3240")
                    : isFocused
                        ? Color.FromHex("#00D4FF")
                        : Color.FromHex("#2C3A4D"),
                BorderThickness = new Thickness(1)
            }
        };

        var row = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 0
        };

        var tabBtn = new ContentButton
        {
            Text = name,
            Icon = icon,
            IconSize = iconSize,
            IconModulate = Color.White,
            Pressed = isFocused,
            Disabled = disabled,
            Borderless = true
        };
        tabBtn.Label.Margin = new Thickness(onClose != null ? 6 : 8, 4, onClose != null ? 4 : 8, 4);
        tabBtn.OnPressed += _ => onSelect();
        row.AddChild(tabBtn);

        if (onClose != null)
        {
            var closeBtn = new ContentButton
            {
                Text = "x",
                MinWidth = 20,
                Disabled = disabled,
                Borderless = true,
                HoverColorOverride = Color.FromHex("#FF555538")
            };
            closeBtn.Label.Margin = new Thickness(4, 4, 6, 4);
            closeBtn.OnPressed += _ => onClose();
            row.AddChild(closeBtn);
        }

        pill.AddChild(row);
        return pill;
    }

    private void RebuildDesktopAppGrid(OsUiState state)
    {
        _desktopAppsGrid.RemoveAllChildren();
        var anyApp = false;

        foreach (var exec in state.InstalledExecutables)
        {
            if (exec.IsBootProgram)
                continue;

            anyApp = true;
            var iconTex = _icons.ResolveProgramIcon(exec);

            var tilePanel = new ContentPanelContainer
            {
                Name = $"AppTile_{exec.PrototypeId}",
                MinWidth = 178f,
                MaxWidth = 210f,
                RectClipContent = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = exec.IsRunning ? Color.FromHex("#112638") : Color.FromHex("#0D1723"),
                    BorderColor = exec.IsRunning ? Color.FromHex("#00C8EE") : Color.FromHex("#1F3950"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 8,
                    ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 8,
                    ContentMarginBottomOverride = 8
                }
            };

            var tileCol = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 6
            };

            var topRow = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8
            };

            var iconFrame = new ContentPanelContainer
            {
                MinSize = new Vector2(36, 36),
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#09111B"),
                    BorderColor = exec.IsRunning ? Color.FromHex("#1E6F8C") : Color.FromHex("#1A2D40"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 4,
                    ContentMarginRightOverride = 4,
                    ContentMarginTopOverride = 4,
                    ContentMarginBottomOverride = 4
                }
            };
            if (iconTex != null)
            {
                iconFrame.AddChild(new ContentTextureRect
                {
                    Name = "AppTileIcon",
                    Texture = iconTex,
                    DisplaySize = new Vector2(26, 26),
                    HorizontalAlignment = HAlignment.Center,
                    VerticalAlignment = VAlignment.Center
                });
            }
            topRow.AddChild(iconFrame);

            var textCol = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                VerticalAlignment = VAlignment.Center,
                SeparationOverride = 2
            };
            textCol.AddChild(new ContentLabel
            {
                Text = exec.Name,
                WordWrap = false,
                ClipText = true,
                HorizontalExpand = true,
                FontColorOverride = exec.IsRunning ? Color.FromHex("#99F3FF") : Color.FromHex("#E6F4FA")
            });
            textCol.AddChild(new ContentLabel
            {
                Text = exec.IsRunning
                    ? $"ACTIVE  •  {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRamQb(exec.RamUsageMb)}"
                    : Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRamQb(exec.RamUsageMb),
                WordWrap = false,
                ClipText = true,
                HorizontalExpand = true,
                FontColorOverride = exec.IsRunning ? Color.FromHex("#66EEAA") : Color.FromHex("#7292A8")
            });
            topRow.AddChild(textCol);
            tileCol.AddChild(topRow);

            var actionRow = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var openBtn = new ContentButton
                {
                    Text = "Open",
                    HorizontalExpand = true,
                    Pressed = true
                };
                openBtn.OnPressed += _ => SendProgramMessage(new OsSwitchProgramMessage(runEnt));
                actionRow.AddChild(openBtn);

                var closeBtn = new ContentButton
                {
                    Text = "x",
                    MinWidth = 26,
                    ToolTip = $"Close {exec.Name}"
                };
                closeBtn.OnPressed += _ => SendProgramMessage(new OsCloseProgramMessage(runEnt));
                actionRow.AddChild(closeBtn);
            }
            else
            {
                var path = exec.FilePath;
                var launchBtn = new ContentButton
                {
                    Text = "Launch",
                    HorizontalExpand = true
                };
                launchBtn.OnPressed += _ => SendProgramMessage(new OsLaunchProgramMessage(path));
                actionRow.AddChild(launchBtn);
            }

            tileCol.AddChild(actionRow);
            tilePanel.AddChild(tileCol);
            _desktopAppsGrid.AddChild(tilePanel);
        }

        if (!anyApp)
        {
            _desktopAppsGrid.AddChild(new ContentLabel
            {
                Text = "No applications installed on hard drive.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
        }
    }

    private void RebuildSystemView(OsUiState state)
    {
        var hw = state.Hardware;
        var netStr = hw.HasNetworkCard
            ? $"{Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRateQb(hw.BandwidthBps)}{(!string.IsNullOrEmpty(hw.NetworkLabel) ? $" ({hw.NetworkLabel})" : string.Empty)}"
            : "NONE";
        _hwDetailsLabel.Text =
            $"Host: {hw.HostName}   |   GUID: {hw.DeviceGuid}\n" +
            $"CPU: {hw.CpuMhz} MHz (Max {hw.MaxPrograms} apps)   |   RAM: {hw.RamUsedMb} / {hw.RamTotalMb} QB\n" +
            $"Storage: {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(hw.StorageUsedBytes)} / {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(hw.StorageTotalBytes)}   |   NIC: {netStr}\n" +
            $"DataPort: {(hw.HasDataPort ? "PRESENT" : "NONE")}   |   Remote Control: {(hw.AllowRemoteControl ? "ENABLED" : "DISABLED")}   |   DeviceLink: {(hw.AllowDeviceLink ? "ENABLED" : "DISABLED")}\n" +
            $"Inserted Disk: {state.InsertedDiskName ?? "None"}";

        _slotActionsWrap.RemoveAllChildren();
        if (hw.HasDiskSlot || !string.IsNullOrWhiteSpace(state.InsertedDiskName))
        {
            var ejDisk = new ContentButton
            {
                Text = "Eject Disk",
                Icon = _icons.TryLoadTexture(IconEject),
                IconSize = new Vector2(14, 14),
                Disabled = string.IsNullOrWhiteSpace(state.InsertedDiskName)
            };
            ejDisk.OnPressed += _ => SendProgramMessage(new OsHardwareActionMessage("eject_disk"));
            _slotActionsWrap.AddChild(ejDisk);
        }
        if (hw.HasIdSlot)
        {
            var ejId = new ContentButton
            {
                Text = "Eject ID",
                Icon = _icons.TryLoadTexture(IconEject),
                IconSize = new Vector2(14, 14)
            };
            ejId.OnPressed += _ => SendProgramMessage(new OsHardwareActionMessage("eject_id"));
            _slotActionsWrap.AddChild(ejId);
        }

        PopulateInstalledComponents(_componentsContainer, _componentsHeaderLabel, hw.InstalledComponents);

        _portsContainer.RemoveAllChildren();
        if (state.DevicePorts.Count == 0)
        {
            _portsContainer.AddChild(new ContentLabel
            {
                Text = "No device link ports available.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        foreach (var port in state.DevicePorts)
        {
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
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

            var trigBtn = new ContentButton { Text = "Pulse", MinWidth = 58 };
            var portId = port.PortId;
            var isHost = port.IsHostPort;
            trigBtn.OnPressed += _ => SendProgramMessage(new OsInvokeDevicePortMessage(portId, isHost, "status"));
            row.AddChild(trigBtn);

            _portsContainer.AddChild(row);
        }
    }

    internal static void PopulateInstalledComponents(
        ContentBoxContainer container,
        ContentLabel headerLabel,
        List<OsHardwareComponentDto> components)
    {
        headerLabel.Text = $"INSTALLED COMPONENTS ({components.Count})";
        container.RemoveAllChildren();

        if (components.Count == 0)
        {
            container.AddChild(new ContentLabel
            {
                Text = "No hardware components detected.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        foreach (var comp in components)
        {
            var badgeHex = comp.Category switch
            {
                "CPU" => "#00D4FF",
                "RAM" => "#66EEAA",
                "HDD" => "#FFB74D",
                "NIC" => "#7C9EFF",
                "ID" => "#E080FF",
                "DISK" => "#FFD54F",
                "BAT" => "#AED581",
                "PORT" => "#80CBC4",
                _ => "#90A4AE"
            };
            var badgeColor = Color.FromHex(badgeHex);
            var statusColor = comp.IsHealthy ? Color.FromHex("#66EEAA") : Color.FromHex("#FF6666");

            var rowPanel = new ContentPanelContainer
            {
                Name = $"InstalledComponentRow_{comp.Category}",
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#0A131E"),
                    BorderColor = Color.FromHex("#1A354B"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 8,
                    ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 5,
                    ContentMarginBottomOverride = 5
                }
            };

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8
            };

            var catPill = new ContentPanelContainer
            {
                MinWidth = 48,
                VerticalAlignment = VAlignment.Center,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = new Color(badgeColor.R, badgeColor.G, badgeColor.B, 0.16f),
                    BorderColor = badgeColor,
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 5,
                    ContentMarginRightOverride = 5,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2
                }
            };
            catPill.AddChild(new ContentLabel
            {
                Name = "InstalledComponentCategoryLabel",
                Text = comp.Category,
                WordWrap = false,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = badgeColor
            });
            row.AddChild(catPill);

            var infoCol = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 1
            };
            infoCol.AddChild(new ContentLabel
            {
                Name = "InstalledComponentNameLabel",
                Text = $"{comp.Name}  [{comp.SlotName}]",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#E6F7FF")
            });
            infoCol.AddChild(new ContentLabel
            {
                Name = "InstalledComponentDetailsLabel",
                Text = comp.Details,
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#8EB4C8")
            });
            row.AddChild(infoCol);

            var statusPill = new ContentPanelContainer
            {
                MinWidth = 68,
                VerticalAlignment = VAlignment.Center,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = new Color(statusColor.R, statusColor.G, statusColor.B, 0.14f),
                    BorderColor = statusColor,
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 6,
                    ContentMarginRightOverride = 6,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2
                }
            };
            statusPill.AddChild(new ContentLabel
            {
                Name = "InstalledComponentStatusLabel",
                Text = comp.Status,
                WordWrap = false,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = statusColor
            });
            row.AddChild(statusPill);

            rowPanel.AddChild(row);
            container.AddChild(rowPanel);
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
/// Boot OS shell for embedded wall/machine controllers and dedicated NTOS consoles
/// (APC, AirAlarm, Autolathe, Station Records, Criminal Records, Surveillance Camera Monitor).
/// Uses a single compact top tab bar so maximum viewport space is dedicated to the active app.
/// </summary>
public sealed class EmbeddedOsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private const string ApcAppPath = "/bin/apc_control.nte";
    private const string AtmosAppPath = "/bin/atmos_control.nte";
    private const string LatheAppPath = "/bin/lathe_control.nte";
    private const string TerminalAppPath = "/bin/terminal.nte";
    private const string IconSystem = "/Textures/Interface/VerbIcons/settings.svg.192dpi.png";
    private const string IconClock = "/Textures/Interface/VerbIcons/clock.svg.192dpi.png";

    private readonly IEntityManager _entMan;
    private readonly ProgramIconResolver _icons;
    private readonly ContentWrapContainer _topTabBar;
    private readonly ContentLabel _clockBadgeLabel;
    private readonly ContentButton _osAuthBtn;
    private bool _hasOsAuth;
    private int _lastStationSeconds = -1;
    private readonly ContentButton _simpleControllerTabBtn;
    private readonly ContentButton _simpleTerminalTabBtn;
    private readonly ContentButton _appViewBtn;
    private readonly ContentButton _listViewBtn;
    private readonly ContentButton _linkViewBtn;
    private readonly List<Control> _inlineRunningTabs = new();
    private readonly ContentBoxContainer _childViewport;
    private readonly ContentBoxContainer _appsView;
    private readonly ContentBoxContainer _portsView;
    private readonly ContentWrapContainer _appsGrid;
    private readonly ContentLabel _sysDetailsLabel;
    private readonly ContentLabel _embeddedComponentsHeaderLabel;
    private readonly ContentBoxContainer _embeddedComponentsContainer;
    private readonly ContentBoxContainer _portsContainer;
    private readonly HardwareLockOverlayControl _lockOverlay;
    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private NetEntity? _activeChildNet;
    private NetEntity? _defaultAppRunningEnt;
    private NetEntity? _terminalRunningEnt;
    private string _defaultAppPath = ApcAppPath;

    public EmbeddedOsUiControl()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
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
            Margin = new Thickness(5),
            SeparationOverride = 5,
            RectClipContent = true
        };

        // Single unified tab bar (eliminates stacked header + tab + running-apps + status bar clutter)
        var barPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            RectClipContent = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1722"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 5,
                ContentMarginRightOverride = 5,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3
            }
        };

        _topTabBar = new ContentWrapContainer
        {
            Name = "EmbeddedTabBar",
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };

        _simpleControllerTabBtn = new ContentButton { Name = "ApcTabControllerButton", Text = "Controller", MinWidth = 84, Visible = false };
        _simpleControllerTabBtn.OnPressed += _ =>
        {
            if (_defaultAppRunningEnt is { } ent)
                SendProgramMessage(new EmbeddedOsSwitchProgramMessage(ent));
            else
                SendProgramMessage(new EmbeddedOsLaunchProgramMessage(_defaultAppPath));
        };

        _simpleTerminalTabBtn = new ContentButton { Name = "ApcTabTerminalButton", Text = "Terminal", MinWidth = 84, Visible = false };
        _simpleTerminalTabBtn.OnPressed += _ =>
        {
            if (_terminalRunningEnt is { } ent)
                SendProgramMessage(new EmbeddedOsSwitchProgramMessage(ent));
            else
                SendProgramMessage(new EmbeddedOsLaunchProgramMessage(TerminalAppPath));
        };

        _appViewBtn = new ContentButton { Text = "Console", MinWidth = 72 };
        _appViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(0));

        _listViewBtn = new ContentButton { Text = "Apps", MinWidth = 60 };
        _listViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(1));

        _linkViewBtn = new ContentButton
        {
            Text = "System",
            Icon = _icons.TryLoadTexture(IconSystem),
            IconSize = new Vector2(14, 14),
            MinWidth = 76
        };
        _linkViewBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsSetViewMessage(2));

        _topTabBar.AddChild(_simpleControllerTabBtn);
        _topTabBar.AddChild(_simpleTerminalTabBtn);
        _topTabBar.AddChild(_appViewBtn);
        _topTabBar.AddChild(_listViewBtn);
        _topTabBar.AddChild(_linkViewBtn);

        var barRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        var embClockBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4
        };
        var embClockTex = _icons.TryLoadTexture(IconClock);
        if (embClockTex != null)
        {
            embClockBox.AddChild(new ContentTextureRect
            {
                Texture = embClockTex,
                DisplaySize = new Vector2(13, 13),
                Modulate = Color.FromHex("#00D4FF"),
                VerticalAlignment = VAlignment.Center
            });
        }
        _clockBadgeLabel = new ContentLabel
        {
            Name = "EmbeddedClockLabel",
            Text = "00:00:00",
            WordWrap = false,
            FontColorOverride = Color.FromHex("#88DCE8"),
            VerticalAlignment = VAlignment.Center
        };
        embClockBox.AddChild(_clockBadgeLabel);

        _osAuthBtn = new ContentButton
        {
            Name = "EmbeddedOsAuthButton",
            Text = "Swipe ID",
            ToolTip = "Swipe ID card to authenticate OS session",
            VerticalAlignment = VAlignment.Center
        };
        _osAuthBtn.OnPressed += _ =>
        {
            if (_hasOsAuth)
                SendProgramMessage(new EmbeddedOsLogoutAuthMessage());
            else
                SendProgramMessage(new EmbeddedOsSwipeAuthMessage());
        };

        var shutDownBtn = new ContentButton
        {
            Name = "EmbeddedOsShutDownButton",
            Text = "Shut Down",
            SetWidth = 100,
            VerticalAlignment = VAlignment.Top
        };
        shutDownBtn.OnPressed += _ => SendProgramMessage(new EmbeddedOsShutDownMessage());

        barRow.AddChild(_topTabBar);
        barRow.AddChild(_osAuthBtn);
        barRow.AddChild(embClockBox);
        barRow.AddChild(shutDownBtn);
        barPanel.AddChild(barRow);
        root.AddChild(barPanel);

        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true
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
        _appsGrid = new ContentWrapContainer
        {
            HorizontalExpand = true,
            HorizontalSeparation = 8,
            VerticalSeparation = 8
        };
        appScroll.AddChild(_appsGrid);
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
        var pInner = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _sysDetailsLabel = new ContentLabel
        {
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#B8E0EE")
        };
        pInner.AddChild(_sysDetailsLabel);
        _embeddedComponentsHeaderLabel = new ContentLabel
        {
            Name = "EmbeddedInstalledComponentsHeader",
            Text = "INSTALLED COMPONENTS (0)",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };
        pInner.AddChild(_embeddedComponentsHeaderLabel);
        _embeddedComponentsContainer = new ContentBoxContainer
        {
            Name = "SystemInstalledComponentsContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        pInner.AddChild(_embeddedComponentsContainer);
        _portsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        pInner.AddChild(_portsContainer);
        pScroll.AddChild(pInner);
        _portsView.AddChild(pScroll);
        root.AddChild(_portsView);

        _lockOverlay = new HardwareLockOverlayControl
        {
            Visible = false
        };

        AddChild(root);
        AddChild(_lockOverlay);
        UpdateEmbeddedClock(force: true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateEmbeddedClock(force: false);
    }

    private void UpdateEmbeddedClock(bool force)
    {
        var stationTime = _entMan.TrySystem<SharedClockSystem>(out var clock)
            ? clock.GetStationTime()
            : TimeSpan.Zero;
        if (stationTime < TimeSpan.Zero)
            stationTime = TimeSpan.Zero;

        var totalSeconds = (int) stationTime.TotalSeconds;
        if (!force && totalSeconds == _lastStationSeconds)
            return;

        _lastStationSeconds = totalSeconds;
        _clockBadgeLabel.Text = $"{stationTime.Hours:D2}:{stationTime.Minutes:D2}:{stationTime.Seconds:D2}";
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not EmbeddedOsUiState state)
            return;

        _activeChildNet = state.ActiveChildProgram;
        var hw = state.Hardware;
        var isSimpleTwoTabMode = state.IsDedicatedPanel;

        if (!string.IsNullOrWhiteSpace(state.DefaultAppPath))
            _defaultAppPath = state.DefaultAppPath;

        _defaultAppRunningEnt = null;
        _terminalRunningEnt = null;
        foreach (var rp in state.RunningPrograms)
        {
            if (rp.IsBootProgram || rp.RunningEntity is not { } rEnt)
                continue;

            if (string.Equals(rp.FilePath, _defaultAppPath, StringComparison.OrdinalIgnoreCase))
                _defaultAppRunningEnt = rEnt;
            else if (string.Equals(rp.FilePath, TerminalAppPath, StringComparison.OrdinalIgnoreCase))
                _terminalRunningEnt = rEnt;
        }

        // Hardware lock overlay is rendered at the ComputerDisplayWindow level for all hosts.
        _lockOverlay.Visible = false;

        _hasOsAuth = !string.IsNullOrWhiteSpace(state.OsAuthLabel);
        if (isSimpleTwoTabMode)
        {
            _osAuthBtn.Visible = false;
        }
        else if (_hasOsAuth)
        {
            _osAuthBtn.Visible = true;
            _osAuthBtn.Text = $"Logout: {state.OsAuthLabel}";
            _osAuthBtn.Disabled = false;
            _osAuthBtn.ToolTip = "Clear active OS user authentication session";
        }
        else if (hw.HasInsertedIdCard)
        {
            _osAuthBtn.Visible = false;
        }
        else
        {
            _osAuthBtn.Visible = true;
            _osAuthBtn.Text = "Swipe ID";
            _osAuthBtn.Disabled = !hw.HasIdSlot;
            _osAuthBtn.ToolTip = hw.HasIdSlot
                ? "Swipe ID card to authenticate OS session"
                : "No ID card reader installed";
        }

        ClearInlineRunningTabs();

        if (isSimpleTwoTabMode)
        {
            // Simplified controller layout (APC, AirAlarm, Autolathe): strictly 2 tabs (Controller & Terminal), no hardware stats bar.
            var terminalActive = _terminalRunningEnt != null && state.ActiveChildProgram == _terminalRunningEnt;

            _simpleControllerTabBtn.Visible = true;
            _simpleTerminalTabBtn.Visible = true;
            _simpleControllerTabBtn.Pressed = !terminalActive;
            _simpleTerminalTabBtn.Pressed = terminalActive;
            _simpleControllerTabBtn.Disabled = state.IsHostLocked;
            _simpleTerminalTabBtn.Disabled = state.IsHostLocked;

            _appViewBtn.Visible = false;
            _listViewBtn.Visible = false;
            _linkViewBtn.Visible = false;

            _childViewport.Visible = true;
            _appsView.Visible = false;
            _portsView.Visible = false;

            SyncVisibleChildProgram();
            return;
        }

        _simpleControllerTabBtn.Visible = false;
        _simpleTerminalTabBtn.Visible = false;
        _appViewBtn.Visible = false; // Running apps live directly inline on the single tab bar!
        _listViewBtn.Visible = true;
        _linkViewBtn.Visible = true;

        _listViewBtn.Pressed = state.CurrentView == 1;
        _linkViewBtn.Pressed = state.CurrentView == 2;
        _listViewBtn.Disabled = state.IsHostLocked;
        _linkViewBtn.Disabled = state.IsHostLocked;

        _childViewport.Visible = state.CurrentView == 0;
        _appsView.Visible = state.CurrentView == 1;
        _portsView.Visible = state.CurrentView == 2;

        // Add running app tabs inline on the SAME single bar with their icons
        foreach (var rp in state.RunningPrograms)
        {
            if (rp.IsBootProgram || rp.RunningEntity is not { } ent)
                continue;

            var isFocused = state.CurrentView == 0 && state.ActiveChildProgram == ent;
            var canClose = !string.Equals(rp.FilePath, _defaultAppPath, StringComparison.OrdinalIgnoreCase);
            var tabPill = OsUiControl.CreateRunningAppTabPill(
                rp.Name,
                _icons.ResolveProgramIcon(rp),
                new Vector2(14, 14),
                isFocused,
                disabled: state.IsHostLocked,
                onSelect: () => SendProgramMessage(new EmbeddedOsSwitchProgramMessage(ent)),
                onClose: canClose ? () => SendProgramMessage(new EmbeddedOsCloseProgramMessage(ent)) : null);

            _inlineRunningTabs.Add(tabPill);
            _topTabBar.AddChild(tabPill);
        }

        var nicText = hw.HasNetworkCard
            ? (!string.IsNullOrEmpty(hw.NetworkLabel) ? $"UP ({hw.NetworkLabel})" : "UP")
            : "NONE";
        _sysDetailsLabel.Text =
            $"Host: {state.HostName}   |   GUID: {state.DeviceGuid}\n" +
            $"CPU: {hw.CpuMhz} MHz   |   RAM: {hw.RamUsedMb}/{hw.RamTotalMb} QB   |   HDD: {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(hw.StorageUsedBytes)}   |   NIC: {nicText}   |   DataPort: {(hw.HasDataPort ? "OK" : "NO")}";

        OsUiControl.PopulateInstalledComponents(_embeddedComponentsContainer, _embeddedComponentsHeaderLabel, hw.InstalledComponents);

        _appsGrid.RemoveAllChildren();
        foreach (var exec in state.InstalledExecutables)
        {
            if (exec.IsBootProgram)
                continue;

            var iconTex = _icons.ResolveProgramIcon(exec);
            var card = new ContentPanelContainer
            {
                MinWidth = 172f,
                MaxWidth = 210f,
                RectClipContent = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = exec.IsRunning ? Color.FromHex("#112638") : Color.FromHex("#0D1723"),
                    BorderColor = exec.IsRunning ? Color.FromHex("#00C8EE") : Color.FromHex("#1F3950"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 8,
                    ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 6,
                    ContentMarginBottomOverride = 6
                }
            };
            var col = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 5
            };
            var head = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
            };
            if (iconTex != null)
            {
                head.AddChild(new ContentTextureRect
                {
                    Texture = iconTex,
                    DisplaySize = new Vector2(22, 22),
                    VerticalAlignment = VAlignment.Center
                });
            }
            head.AddChild(new ContentLabel
            {
                Text = exec.Name,
                WordWrap = false,
                ClipText = true,
                HorizontalExpand = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#E6F4FA")
            });
            col.AddChild(head);

            if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var sw = new ContentButton { Text = "Open", HorizontalExpand = true, Pressed = true, Disabled = state.IsHostLocked };
                sw.OnPressed += _ => SendProgramMessage(new EmbeddedOsSwitchProgramMessage(runEnt));
                col.AddChild(sw);
            }
            else
            {
                var path = exec.FilePath;
                var run = new ContentButton { Text = "Launch", HorizontalExpand = true, Disabled = state.IsHostLocked };
                run.OnPressed += _ => SendProgramMessage(new EmbeddedOsLaunchProgramMessage(path));
                col.AddChild(run);
            }

            card.AddChild(col);
            _appsGrid.AddChild(card);
        }

        _portsContainer.RemoveAllChildren();
        foreach (var port in state.MirroredPorts)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
            row.AddChild(new ContentLabel
            {
                Text = $"[{(port.IsHostPort ? "HOST" : "COMP")} {(port.IsSource ? "OUT" : "IN ")}] {port.PortId} -> {(port.LinkedTargets.Count > 0 ? string.Join(", ", port.LinkedTargets) : "(none)")}",
                HorizontalExpand = true
            });
            var pulse = new ContentButton { Text = "Status", MinWidth = 60, Disabled = state.IsHostLocked };
            var portId = port.PortId;
            var isHost = port.IsHostPort;
            pulse.OnPressed += _ => SendProgramMessage(new EmbeddedOsInvokePortMessage(portId, isHost, "status"));
            row.AddChild(pulse);
            _portsContainer.AddChild(row);
        }

        SyncVisibleChildProgram();
    }

    private void ClearInlineRunningTabs()
    {
        foreach (var tab in _inlineRunningTabs)
        {
            if (!tab.Disposed && tab.Parent == _topTabBar)
                _topTabBar.RemoveChild(tab);
        }
        _inlineRunningTabs.Clear();
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
