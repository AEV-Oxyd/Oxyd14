using System.Collections.Generic;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.GameTicking;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated handheld Operating System shell for PDAs (/bin/pda_os.nte -> PdaOsProgramBui).
/// Features a single unified tab strip for both system menus (Home, Device) and running apps,
/// top-bar clickable pocket utility icons, a merged Home view (round time, owner/ID & VFS apps),
/// and keeps hardware diagnostics tucked inside the Device tab.
/// </summary>
public sealed class PdaOsUiControl : ProgramUiControl, IOsProgramUiHost
{
    private const string IconHome = "/Textures/Interface/home.png";
    private const string IconDevice = "/Textures/Interface/VerbIcons/settings.svg.192dpi.png";
    private const string IconClock = "/Textures/Interface/VerbIcons/clock.svg.192dpi.png";
    private const string IconFlashlight = "/Textures/Interface/VerbIcons/light.svg.192dpi.png";
    private const string IconRingtone = "/Textures/Interface/VerbIcons/signal.svg.192dpi.png";
    private const string IconMusic = "/Textures/Interface/Actions/pai-midi.png";
    private const string IconUplink = "/Textures/Interface/Actions/shop.png";
    private const string IconLock = "/Textures/Interface/VerbIcons/lock.svg.192dpi.png";
    private const string IconEject = "/Textures/Interface/VerbIcons/eject.svg.192dpi.png";

    private readonly IEntityManager _entMan;
    private readonly IGameTiming _timing;
    private readonly IResourceCache _resourceCache;
    private readonly ProgramIconResolver _icons;
    private readonly Dictionary<string, Texture?> _iconCache = new();

    private readonly ContentWrapContainer _unifiedTabBar;
    private readonly ContentButton _homeTabBtn;
    private readonly ContentButton _deviceTabBtn;
    private readonly List<Control> _runningAppTabs = new();
    private readonly ContentBoxContainer _utilitiesBar;

    private readonly ContentScrollContainer _homeScroll;
    private readonly ContentLabel _roundTimeLabel;
    private readonly ContentLabel _stationAlertLabel;
    private readonly ContentLabel _ownerLabel;
    private readonly ContentLabel _insertedIdLabel;
    private readonly ContentButton _homeEjectIdBtn;
    private readonly ContentBoxContainer _appsListContainer;

    private readonly ContentBoxContainer _deviceView;
    private readonly ContentLabel _slotsSummaryLabel;
    private readonly ContentWrapContainer _slotActionsWrap;
    private readonly ContentLabel _hwDetailsLabel;
    private readonly ContentBoxContainer _portsContainer;

    private readonly ContentBoxContainer _childViewport;

    private readonly Dictionary<EntityUid, ProgramUiControl> _mountedChildren = new();
    private NetEntity? _activeChildNet;
    private int _lastRoundSeconds = -1;

    public PdaOsUiControl()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _resourceCache = IoCManager.Resolve<IResourceCache>();
        _icons = new ProgramIconResolver(
            _entMan,
            IoCManager.Resolve<IPrototypeManager>(),
            _resourceCache,
            IoCManager.Resolve<IComponentFactory>());

        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6)
        };

        // Top Unified Chrome: Single Tab Bar (Home, Device, Running Apps) + Pocket Utility Icons on right.
        // Hardware stats are intentionally omitted here and shown only inside the Device view / hardware apps.
        var topBarPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1722"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 5,
                ContentMarginRightOverride = 5,
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
            Name = "UnifiedTabBar",
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };

        _homeTabBtn = new ContentButton
        {
            Name = "TabHomeButton",
            Text = "Home",
            Icon = TryLoadIcon(IconHome),
            IconSize = new Vector2(14, 14),
            Pressed = true
        };
        _homeTabBtn.OnPressed += _ => SendProgramMessage(new PdaOsSetViewMessage(0));

        _deviceTabBtn = new ContentButton
        {
            Name = "TabDeviceButton",
            Text = "Device",
            Icon = TryLoadIcon(IconDevice),
            IconSize = new Vector2(14, 14)
        };
        _deviceTabBtn.OnPressed += _ => SendProgramMessage(new PdaOsSetViewMessage(1));

        _unifiedTabBar.AddChild(_homeTabBtn);
        _unifiedTabBar.AddChild(_deviceTabBtn);

        _utilitiesBar = new ContentBoxContainer
        {
            Name = "PocketUtilitiesBar",
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Top,
            SeparationOverride = 4
        };

        var shutDownBtn = new ContentButton
        {
            Name = "PdaShutDownButton",
            Text = "Shut Down",
            SetWidth = 76,
            VerticalAlignment = VAlignment.Top
        };
        shutDownBtn.OnPressed += _ => SendProgramMessage(new PdaOsShutDownMessage());

        topBarRow.AddChild(_unifiedTabBar);
        topBarRow.AddChild(_utilitiesBar);
        topBarRow.AddChild(shutDownBtn);
        topBarPanel.AddChild(topBarRow);
        root.AddChild(topBarPanel);

        // View 0: Merged Home (Round Time, Owner/Inserted ID & All VFS Applications)
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

        var homeHeaderPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1A26"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        var homeHeaderCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };

        var timeRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        var clockIcon = new ContentTextureRect
        {
            Texture = TryLoadIcon(IconClock),
            DisplaySize = new Vector2(14, 14),
            Modulate = Color.FromHex("#00D4FF")
        };

        _roundTimeLabel = new ContentLabel
        {
            Name = "RoundTimeLabel",
            Text = "Round Time: 00:00:00",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };

        timeRow.AddChild(clockIcon);
        timeRow.AddChild(_roundTimeLabel);
        homeHeaderCol.AddChild(timeRow);

        _stationAlertLabel = new ContentLabel
        {
            Text = "Station: Unassigned | Alert: Standard",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFE066")
        };
        homeHeaderCol.AddChild(_stationAlertLabel);

        _ownerLabel = new ContentLabel
        {
            Name = "OwnerLabel",
            Text = "Name: Unregistered",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA")
        };
        homeHeaderCol.AddChild(_ownerLabel);

        var idRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _insertedIdLabel = new ContentLabel
        {
            Name = "InsertedIdLabel",
            Text = "Inserted ID: None",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#B8E0E8")
        };

        _homeEjectIdBtn = MakeIconButton(
            name: "HomeEjectIdButton",
            iconPath: IconEject,
            fallbackText: "Eject",
            tooltip: "Eject ID Card",
            onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("eject_id")));
        _homeEjectIdBtn.Visible = false;

        idRow.AddChild(_insertedIdLabel);
        idRow.AddChild(_homeEjectIdBtn);
        homeHeaderCol.AddChild(idRow);

        homeHeaderPanel.AddChild(homeHeaderCol);
        homeContent.AddChild(homeHeaderPanel);

        homeContent.AddChild(new ContentLabel
        {
            Text = "APPLICATIONS:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        _appsListContainer = new ContentBoxContainer
        {
            Name = "HomeAppsList",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        homeContent.AddChild(_appsListContainer);

        _homeScroll.AddChild(homeContent);
        root.AddChild(_homeScroll);

        // View 1: Device Diagnostics, PDA Slots & Mirrored DeviceLink Ports
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

        deviceCol.AddChild(new ContentLabel
        {
            Text = "PDA SLOTS & CARTRIDGES:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 2)
        });

        _slotsSummaryLabel = new ContentLabel
        {
            Text = "ID: Empty | Pen: Empty | Disk: Empty",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#D6F5F5"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        deviceCol.AddChild(_slotsSummaryLabel);

        _slotActionsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 8)
        };
        deviceCol.AddChild(_slotActionsWrap);

        deviceCol.AddChild(new ContentLabel
        {
            Text = "HARDWARE DIAGNOSTICS:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 2, 0, 2)
        });

        _hwDetailsLabel = new ContentLabel
        {
            Name = "DeviceHardwareDetailsLabel",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#E0F7FA"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        deviceCol.AddChild(_hwDetailsLabel);

        deviceCol.AddChild(new ContentLabel
        {
            Text = "DEVICE LINK PORTS:",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 2, 0, 2)
        });

        _portsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        deviceCol.AddChild(_portsContainer);

        deviceScroll.AddChild(deviceCol);
        _deviceView.AddChild(deviceScroll);
        root.AddChild(_deviceView);

        // View 2: Parented Child Program Viewport
        _childViewport = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        root.AddChild(_childViewport);

        AddChild(root);
        UpdateRoundTimeLabel(force: true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateRoundTimeLabel(force: false);
    }

    private void UpdateRoundTimeLabel(bool force)
    {
        var elapsed = TimeSpan.Zero;
        if (_entMan.TrySystem<ClientGameTicker>(out var ticker))
        {
            elapsed = _timing.CurTime - ticker.RoundStartTimeSpan;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
        }

        var totalSeconds = (int) elapsed.TotalSeconds;
        if (!force && totalSeconds == _lastRoundSeconds)
            return;

        _lastRoundSeconds = totalSeconds;
        var hours = (int) elapsed.TotalHours;
        _roundTimeLabel.Text = $"Round Time: {hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private Texture? TryLoadIcon(string texturePath)
    {
        if (_iconCache.TryGetValue(texturePath, out var cached))
            return cached;

        Texture? tex = null;
        if (_resourceCache.TryGetResource<TextureResource>(new ResPath(texturePath), out var texRes))
            tex = texRes.Texture;

        _iconCache[texturePath] = tex;
        return tex;
    }

    private Texture? TryLoadSprite(SpriteSpecifier? spec)
    {
        if (spec == null || !_entMan.TrySystem<SpriteSystem>(out var spriteSys))
            return null;

        try
        {
            return spriteSys.Frame0(spec);
        }
        catch
        {
            return null;
        }
    }

    private ContentButton MakeIconButton(
        string name,
        string iconPath,
        string fallbackText,
        string tooltip,
        Action onPressed,
        bool pressed = false,
        Color? iconModulate = null,
        bool disabled = false)
    {
        var icon = TryLoadIcon(iconPath);
        var btn = new ContentButton
        {
            Name = name,
            Icon = icon,
            IconSize = new Vector2(16, 16),
            IconModulate = iconModulate,
            Text = icon == null ? fallbackText : string.Empty,
            ToolTip = tooltip,
            Pressed = pressed,
            Disabled = disabled
        };
        btn.OnPressed += _ => onPressed();
        return btn;
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not PdaOsUiState state)
            return;

        _activeChildNet = state.ActiveChildProgram;

        var activeView = state.CurrentView;
        if (activeView == 2 && state.ActiveChildProgram == null)
            activeView = 0;

        _homeScroll.Visible = activeView == 0;
        _deviceView.Visible = activeView == 1;
        _childViewport.Visible = activeView == 2;

        RebuildUnifiedTabBar(state, activeView);
        RebuildUtilitiesBar(state);
        RebuildHomeView(state);
        RebuildDeviceView(state);
        SyncVisibleChildProgram();
    }

    private void RebuildUnifiedTabBar(PdaOsUiState state, int activeView)
    {
        // System menus and running app tabs share a single unified tab strip.
        _homeTabBtn.Pressed = activeView == 0;
        _deviceTabBtn.Pressed = activeView == 1;

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

            var isFocused = activeView == 2 && prog.RunningEntity == state.ActiveChildProgram;

            var tabPair = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal
            };

            var tabBtn = new ContentButton
            {
                Text = prog.Name,
                Icon = _icons.ResolveProgramIcon(prog),
                IconSize = new Vector2(14, 14),
                IconModulate = Color.White,
                Pressed = isFocused
            };
            tabBtn.OnPressed += _ => SendProgramMessage(new PdaOsSwitchProgramMessage(rEnt));
            tabPair.AddChild(tabBtn);

            var closeBtn = new ContentButton
            {
                Text = "x",
                MinWidth = 22,
                Margin = new Thickness(1, 0, 0, 0)
            };
            closeBtn.OnPressed += _ => SendProgramMessage(new PdaOsCloseProgramMessage(rEnt));
            tabPair.AddChild(closeBtn);

            _runningAppTabs.Add(tabPair);
            _unifiedTabBar.AddChild(tabPair);
        }
    }

    private void RebuildUtilitiesBar(PdaOsUiState state)
    {
        _utilitiesBar.RemoveAllChildren();

        _utilitiesBar.AddChild(MakeIconButton(
            name: "UtilityFlashlightButton",
            iconPath: IconFlashlight,
            fallbackText: "Light",
            tooltip: state.FlashlightOn ? "Flashlight: ON" : "Flashlight: OFF",
            onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("toggle_flashlight")),
            pressed: state.FlashlightOn,
            iconModulate: state.FlashlightOn ? Color.FromHex("#FFE066") : null));

        if (state.HasRinger)
        {
            _utilitiesBar.AddChild(MakeIconButton(
                name: "UtilityRingtoneButton",
                iconPath: IconRingtone,
                fallbackText: "Ring",
                tooltip: "Ringtone",
                onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("show_ringtone"))));
        }

        if (state.HasInstrument)
        {
            _utilitiesBar.AddChild(MakeIconButton(
                name: "UtilityMusicButton",
                iconPath: IconMusic,
                fallbackText: "Music",
                tooltip: "Music",
                onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("show_music"))));
        }

        if (state.HasUplinkUnlocked)
        {
            _utilitiesBar.AddChild(MakeIconButton(
                name: "UtilityUplinkButton",
                iconPath: IconUplink,
                fallbackText: "Uplink",
                tooltip: "Open Uplink",
                onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("show_uplink")),
                iconModulate: Color.FromHex("#FF6666")));

            _utilitiesBar.AddChild(MakeIconButton(
                name: "UtilityLockUplinkButton",
                iconPath: IconLock,
                fallbackText: "Lock",
                tooltip: "Lock Uplink",
                onPressed: () => SendProgramMessage(new PdaOsHardwareActionMessage("lock_uplink")),
                iconModulate: Color.FromHex("#FF8888")));
        }
    }

    private void RebuildHomeView(PdaOsUiState state)
    {
        UpdateRoundTimeLabel(force: true);

        var stationPart = string.IsNullOrWhiteSpace(state.StationName) ? "Unassigned" : state.StationName;
        var alertPart = string.IsNullOrWhiteSpace(state.StationAlertLevel) ? "Standard" : state.StationAlertLevel.ToUpperInvariant();
        _stationAlertLabel.Text = $"Station: {stationPart} | Alert: {alertPart}";
        _stationAlertLabel.FontColorOverride = state.StationAlertColor == Color.White
            ? Color.FromHex("#FFE066")
            : state.StationAlertColor;

        if (!string.IsNullOrWhiteSpace(state.OwnerName))
        {
            var jobSuffix = !string.IsNullOrWhiteSpace(state.OwnerJob) ? $" — {state.OwnerJob}" : string.Empty;
            _ownerLabel.Text = $"Name: {state.OwnerName}{jobSuffix}";
        }
        else
        {
            _ownerLabel.Text = "Name: Unregistered";
        }

        _insertedIdLabel.Text = state.InsertedIdName != null
            ? $"Inserted ID: {state.InsertedIdName}"
            : "Inserted ID: None";
        _homeEjectIdBtn.Visible = state.InsertedIdName != null;

        _appsListContainer.RemoveAllChildren();
        var anyApp = false;
        foreach (var exec in state.InstalledExecutables)
        {
            if (exec.IsBootProgram)
                continue;

            anyApp = true;
            var rowPanel = new ContentPanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = exec.IsRunning ? Color.FromHex("#102230") : Color.FromHex("#0C141E"),
                    BorderColor = exec.IsRunning ? Color.FromHex("#246A8A") : Color.FromHex("#1C2C3D"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 6,
                    ContentMarginRightOverride = 6,
                    ContentMarginTopOverride = 4,
                    ContentMarginBottomOverride = 4
                }
            };

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
            };

            var appIconTex = _icons.ResolveProgramIcon(exec);
            if (appIconTex != null)
            {
                row.AddChild(new ContentTextureRect
                {
                    Texture = appIconTex,
                    DisplaySize = new Vector2(18, 18)
                });
            }

            row.AddChild(new ContentLabel
            {
                Text = $"{exec.Name} [{exec.FilePath}]",
                HorizontalExpand = true,
                FontColorOverride = exec.IsRunning ? Color.FromHex("#88EEFF") : Color.FromHex("#E0F7FA")
            });

            if (exec.IsRunning && exec.RunningEntity is { } runEnt)
            {
                var focusBtn = new ContentButton { Text = "Focus", MinWidth = 56 };
                focusBtn.OnPressed += _ => SendProgramMessage(new PdaOsSwitchProgramMessage(runEnt));
                row.AddChild(focusBtn);

                var stopBtn = new ContentButton { Text = "Stop", MinWidth = 50 };
                stopBtn.OnPressed += _ => SendProgramMessage(new PdaOsCloseProgramMessage(runEnt));
                row.AddChild(stopBtn);
            }
            else
            {
                var path = exec.FilePath;
                var runBtn = new ContentButton { Text = "Run", MinWidth = 56 };
                runBtn.OnPressed += _ => SendProgramMessage(new PdaOsLaunchProgramMessage(path));
                row.AddChild(runBtn);
            }

            rowPanel.AddChild(row);
            _appsListContainer.AddChild(rowPanel);
        }

        if (!anyApp)
        {
            _appsListContainer.AddChild(new ContentLabel
            {
                Text = "No applications installed on VFS.",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#78909C")
            });
        }
    }

    private void RebuildDeviceView(PdaOsUiState state)
    {
        _slotsSummaryLabel.Text =
            $"ID Slot: {state.InsertedIdName ?? "Empty"} | Pen: {state.InsertedPenName ?? "Empty"} | Disk: {state.InsertedDiskName ?? "Empty"}" +
            (state.InsertedPaiName != null ? $" | pAI: {state.InsertedPaiName}" : string.Empty);

        _slotActionsWrap.RemoveAllChildren();
        AddSlotActionBtn(_slotActionsWrap, "Eject ID", "eject_id", disabled: state.InsertedIdName == null);
        AddSlotActionBtn(_slotActionsWrap, "Eject Pen", "eject_pen", disabled: state.InsertedPenName == null);
        AddSlotActionBtn(_slotActionsWrap, "Eject Disk", "eject_disk", disabled: state.InsertedDiskName == null);
        if (state.InsertedPaiName != null)
            AddSlotActionBtn(_slotActionsWrap, "Eject pAI", "eject_pai", disabled: false);

        var hw = state.Hardware;
        var netStr = hw.HasNetworkCard
            ? $"{hw.BandwidthBps / 1024} KB/s{(!string.IsNullOrEmpty(hw.NetworkLabel) ? $" ({hw.NetworkLabel})" : string.Empty)}"
            : "NONE";

        _hwDetailsLabel.Text =
            $"Device: {hw.HostName} | GUID: {hw.DeviceGuid}\n" +
            $"CPU: {hw.CpuMhz} MHz (max {hw.MaxPrograms} apps) | RAM: {hw.RamUsedMb}/{hw.RamTotalMb} MB\n" +
            $"HDD: {hw.StorageUsedBytes / 1024}/{hw.StorageTotalBytes / 1024} KB | NET: {netStr}\n" +
            $"DataPort: {(hw.HasDataPort ? "PRESENT" : "NONE")} | RemoteCtrl: {hw.AllowRemoteControl} | DeviceLink: {hw.AllowDeviceLink} | Flashlight: {(state.FlashlightOn ? "ON" : "OFF")}";

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

    private void AddSlotActionBtn(ContentWrapContainer parent, string text, string action, bool disabled = false)
    {
        var btn = new ContentButton
        {
            Text = text,
            Icon = TryLoadIcon(IconEject),
            IconSize = new Vector2(14, 14),
            Disabled = disabled
        };
        btn.OnPressed += _ => SendProgramMessage(new PdaOsHardwareActionMessage(action));
        parent.AddChild(btn);
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
