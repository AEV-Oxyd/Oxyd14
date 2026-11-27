using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.Eye;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Viewport;
using Content.Client.Viewport;
using Content.Shared._Oxyd.Computers.Cameras;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Reworked NTOS Wi-Fi Surveillance Camera App UI (<c>/bin/cameras.nte</c>).
/// - Lists Wi-Fi cameras grouped by zone via the closest operational StationNetworkRouter.
/// - Streams up to 6 simultaneous live camera feeds in an adaptive grid (1x1, 2x1, 2x2, 3x2) or Solo mode.
/// - Uses native <see cref="ScalingViewport"/> (<see cref="IClydeViewport"/>) at full <c>480x480</c> resolution
///   with <see cref="ScalingViewport.DeferRender"/> staggered scheduling and 100% vanilla lighting/fire/atmos shaders,
///   leveraging the GPU budget freed when stationary consoles pause main world rendering.
/// - Fits every camera viewport strictly 1:1 to the camera's square POV (<c>480x480</c> = 15x15 tiles)
///   with zero dead letterbox bars or oversized wrapper frames.
/// - Preserves full <see cref="Robust.Client.UserInterface.CustomControls.IViewportControl"/> Examine
///   support (Shift+Click / Right-Click) through every active feed.
/// - Keeps the Witness Logs &amp; Live Audio tab collapsed by default so 6x camera grids get 100% of the
///   workspace height; opens on demand when clicking the <c>Logs</c> button in the camera top bar.
/// </summary>
public sealed partial class SurveillanceCameraUiControl : ProgramUiControl
{
    public const float MinCameraFps = 45f;
    public const float MaxCameraRenderInterval = 1f / MinCameraFps; // <= 0.02222s (minimum 45 FPS on all active feeds)
    private const int CameraPovPixels = EyeManager.PixelsPerMeter * 15; // 480x480 full native resolution

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private readonly FixedEye _defaultEye = new();
    private readonly ContentLabel _statusLabel;
    private readonly ContentLabel _authStatusLabel;
    private readonly ContentWrapContainer _authButtonsWrap;
    private readonly ContentWrapContainer _zoneBar;
    private readonly ContentLineEdit _searchEdit;
    private readonly ContentButton _listModeBtn;
    private readonly ContentButton _mapModeBtn;
    private readonly ContentScrollContainer _camScroll;
    private readonly ContentBoxContainer _cameraListContainer;
    private readonly OxydDeviceNavMapControl _mapSelector;
    private bool _mapMode;

    private readonly ContentLabel _activeCamTitleLabel;
    private readonly ContentPanelContainer _audioBadgePanel;
    private readonly StyleBoxFlat _audioBadgeStyle;
    private readonly ContentLabel _audioBadgeLabel;
    private readonly ContentButton _soloToggleBtn;
    private readonly ContentButton _logsToggleBtn;
    private readonly ContentButton _disconnectBtn;

    private readonly CameraFeedGrid _feedGrid;
    private readonly Dictionary<NetEntity, CameraFeedTile> _tilesByNet = new();
    private readonly List<CameraFeedTile> _orderedTiles = new();
    private readonly HashSet<EntityUid> _lerpedCameraUids = new();

    private readonly ContentPanelContainer _inspectorDrawer;
    private readonly ContentPanelContainer _liveAudioPanel;
    private readonly ContentBoxContainer _liveAudioContainer;
    private readonly ContentLabel _logHeaderLabel;
    private readonly ContentButton _downloadLogsBtn;
    private readonly ContentButton _clearLogsBtn;
    private readonly ContentBoxContainer _logsContainer;

    private SurveillanceCameraUiState? _lastState;
    private NetEntity? _soloCamera;
    private bool _logsExpanded;
    private int _renderRoundRobinCursor;
    private float _globalRenderCooldown;
    private float _smoothedFrameTime = 0.016f;

    public int ActiveCameraCount => _orderedTiles.Count;

    /// <summary>
    /// Forces the enclosing <see cref="ComputerDisplayWindow"/> into viewport focus mode (pausing normal
    /// world rendering) only when actively viewing the camera app with more than 2 cameras active.
    /// </summary>
    public bool ShouldForceViewportFocus => VisibleInTree && _feedGrid.Visible && _orderedTiles.Count > 2;

    public SurveillanceCameraUiControl()
    {
        IoCManager.InjectDependencies(this);
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            Margin = new Thickness(7),
            SeparationOverride = 6
        };

        // Compact conditional alert/status strip (only visible when offline or displaying an actionable notice)
        _statusLabel = new ContentLabel
        {
            Name = "CameraAppStatusLabel",
            Text = string.Empty,
            FontColorOverride = Color.FromHex("#FF7A7A"),
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            MinHeight = 18f,
            Visible = false
        };
        root.AddChild(_statusLabel);

        // Main Split: Left Directory | Right Multi-Feed Grid OR On-Demand Logs Tab
        var split = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            SeparationOverride = 8
        };

        // Left: Auth bar + Zone filter + Search + Camera directory list
        var leftPane = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            MinWidth = 228f,
            MaxWidth = 248f,
            VerticalExpand = true,
            RectClipContent = true,
            SeparationOverride = 5
        };

        var authPanel = new ContentPanelContainer
        {
            Name = "CameraAuthPanel",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1824"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };
        var authCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        _authStatusLabel = new ContentLabel
        {
            Name = "CameraAuthStatusLabel",
            Text = "AUTH: LOCKED (Swipe ID or .acc)",
            FontColorOverride = Color.FromHex("#FFD166"),
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true
        };
        _authButtonsWrap = new ContentWrapContainer
        {
            Name = "CameraAuthButtonsWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 3
        };
        authCol.AddChild(_authStatusLabel);
        authCol.AddChild(_authButtonsWrap);
        authPanel.AddChild(authCol);
        leftPane.AddChild(authPanel);

        var zoneScroll = new ContentScrollContainer
        {
            Name = "CameraZoneScroll",
            HorizontalExpand = true,
            MinHeight = 32f,
            MaxHeight = 68f,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _zoneBar = new ContentWrapContainer
        {
            Name = "CameraZoneBar",
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            RectClipContent = true
        };
        zoneScroll.AddChild(_zoneBar);
        leftPane.AddChild(zoneScroll);

        _searchEdit = new ContentLineEdit
        {
            Name = "CameraSearchEdit",
            PlaceHolder = "Filter cameras...",
            HorizontalExpand = true,
            MinHeight = 28f
        };
        _searchEdit.OnTextChanged += _ => RebuildCameraList();
        leftPane.AddChild(_searchEdit);

        var dirModeRow = new ContentWrapContainer
        {
            Name = "CameraViewModeRow",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        _listModeBtn = new ContentButton
        {
            Name = "CameraListModeButton",
            Text = "List",
            Pressed = true,
            MinWidth = 68,
            MinHeight = 24
        };
        _listModeBtn.OnPressed += _ => SetMapMode(false);
        _mapModeBtn = new ContentButton
        {
            Name = "CameraViewModeToggleButton",
            Text = "Map",
            Pressed = false,
            MinWidth = 68,
            MinHeight = 24
        };
        _mapModeBtn.OnPressed += _ => SetMapMode(true);
        dirModeRow.AddChild(_listModeBtn);
        dirModeRow.AddChild(_mapModeBtn);
        leftPane.AddChild(dirModeRow);

        _camScroll = new ContentScrollContainer
        {
            Name = "CameraListScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _cameraListContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            RectClipContent = true,
            SeparationOverride = 4
        };
        _camScroll.AddChild(_cameraListContainer);
        leftPane.AddChild(_camScroll);

        _mapSelector = new OxydDeviceNavMapControl("Camera")
        {
            Visible = false
        };
        _mapSelector.MarkerSelected += key =>
        {
            if (NetEntity.TryParse(key, out var camNet))
                SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.SelectCamera, cameraEntity: camNet));
        };
        leftPane.AddChild(_mapSelector);
        split.AddChild(leftPane);

        // Right: Focused Camera Top Bar + Full-Height 1..6 Camera Feed Grid (or Full-Height Logs Tab on click)
        var rightPane = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            SeparationOverride = 6
        };

        var feedBar = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinHeight = 28f,
            RectClipContent = true,
            SeparationOverride = 6
        };
        _activeCamTitleLabel = new ContentLabel
        {
            Name = "ActiveCameraTitleLabel",
            Text = "No active camera feed",
            FontColorOverride = Color.FromHex("#E0F7FA"),
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            VerticalAlignment = VAlignment.Center
        };
        feedBar.AddChild(_activeCamTitleLabel);

        _audioBadgeStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1A242E"),
            BorderColor = Color.FromHex("#3A5266"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 7,
            ContentMarginRightOverride = 7,
            ContentMarginTopOverride = 3,
            ContentMarginBottomOverride = 3
        };
        _audioBadgePanel = new ContentPanelContainer
        {
            PanelOverride = _audioBadgeStyle,
            VerticalAlignment = VAlignment.Center,
            RectClipContent = true
        };
        _audioBadgeLabel = new ContentLabel
        {
            Name = "CameraAudioBadgeLabel",
            Text = "AUDIO: OFF",
            WordWrap = false,
            FontColorOverride = Color.FromHex("#8899AA")
        };
        _audioBadgePanel.AddChild(_audioBadgeLabel);
        feedBar.AddChild(_audioBadgePanel);

        _soloToggleBtn = new ContentButton
        {
            Name = "CameraSoloToggleBtn",
            Text = "Solo",
            MinWidth = 54,
            MinHeight = 26f,
            Disabled = true
        };
        _soloToggleBtn.OnPressed += _ => ToggleSoloForFocused();
        feedBar.AddChild(_soloToggleBtn);

        _logsToggleBtn = new ContentButton
        {
            Name = "CameraLogsToggleBtn",
            Text = "Logs",
            ToolTip = "Open witness logs and live audio for the focused camera",
            MinWidth = 68,
            MinHeight = 26f,
            Pressed = false
        };
        _logsToggleBtn.OnPressed += _ => ToggleLogsTab();
        feedBar.AddChild(_logsToggleBtn);

        _disconnectBtn = new ContentButton
        {
            Name = "CameraDisconnectBtn",
            Text = "Disconnect",
            MinWidth = 86,
            MinHeight = 26f,
            Disabled = true
        };
        _disconnectBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.Disconnect));
        feedBar.AddChild(_disconnectBtn);
        rightPane.AddChild(feedBar);

        // Adaptive Multi-Camera Feed Grid (gets 100% of right-pane height while Logs tab is closed!)
        _feedGrid = new CameraFeedGrid(_defaultEye, CameraPovPixels)
        {
            Name = "CameraFeedGrid",
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = true
        };
        rightPane.AddChild(_feedGrid);

        // On-Demand Witness Logs & Live Audio Tab (hidden by default; opened only via top-bar Logs button)
        _inspectorDrawer = new ContentPanelContainer
        {
            Name = "CameraInspectorDrawer",
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0A1118"),
                BorderColor = Color.FromHex("#1D394A"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };

        var inspectorBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            RectClipContent = true,
            SeparationOverride = 6
        };

        // Live Audio Transcript Strip (only visible when focused camera has Microphone Upgrade)
        _liveAudioPanel = new ContentPanelContainer
        {
            Name = "CameraLiveAudioPanel",
            HorizontalExpand = true,
            MaxHeight = 86f,
            RectClipContent = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0B1C19"),
                BorderColor = Color.FromHex("#247A5E"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 7,
                ContentMarginRightOverride = 7,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };
        var liveAudioScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _liveAudioContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            RectClipContent = true,
            SeparationOverride = 3
        };
        liveAudioScroll.AddChild(_liveAudioContainer);
        _liveAudioPanel.AddChild(liveAudioScroll);
        inspectorBox.AddChild(_liveAudioPanel);

        // Witness Logs Toolbar
        var logToolbar = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinHeight = 28f,
            RectClipContent = true,
            SeparationOverride = 6
        };
        _logHeaderLabel = new ContentLabel
        {
            Name = "CameraLogHeaderLabel",
            Text = "WITNESS LOGS (0 / 4096 B)",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            VerticalAlignment = VAlignment.Center
        };
        _downloadLogsBtn = new ContentButton
        {
            Name = "CameraDownloadLogsBtn",
            Text = "Download Logs",
            MinWidth = 108,
            MinHeight = 26f,
            Disabled = true
        };
        _downloadLogsBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.DownloadLogs));

        _clearLogsBtn = new ContentButton
        {
            Name = "CameraClearLogsBtn",
            Text = "Clear Memory",
            MinWidth = 98,
            MinHeight = 26f,
            Disabled = true
        };
        _clearLogsBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.ClearLogs));

        logToolbar.AddChild(_logHeaderLabel);
        logToolbar.AddChild(_downloadLogsBtn);
        logToolbar.AddChild(_clearLogsBtn);
        inspectorBox.AddChild(logToolbar);

        var logScroll = new ContentScrollContainer
        {
            Name = "CameraLogsScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _logsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            RectClipContent = true,
            SeparationOverride = 3
        };
        logScroll.AddChild(_logsContainer);
        inspectorBox.AddChild(logScroll);

        _inspectorDrawer.AddChild(inspectorBox);
        rightPane.AddChild(_inspectorDrawer);

        split.AddChild(rightPane);
        root.AddChild(split);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not SurveillanceCameraUiState state)
            return;

        _lastState = state;

        if (!state.Online)
        {
            _statusLabel.Text = "OFFLINE — no station network router reachable.";
            _statusLabel.ToolTip = _statusLabel.Text;
            _statusLabel.FontColorOverride = Color.FromHex("#FF7A7A");
            _statusLabel.Visible = true;
        }
        else if (!string.IsNullOrWhiteSpace(state.StatusMessage) &&
                 (state.StatusMessage.Contains("full", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("lost", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("Authorized", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("Cannot", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("Downloaded", StringComparison.OrdinalIgnoreCase) ||
                  state.StatusMessage.Contains("Cleared", StringComparison.OrdinalIgnoreCase)))
        {
            _statusLabel.Text = state.StatusMessage;
            _statusLabel.ToolTip = state.StatusMessage;
            _statusLabel.FontColorOverride = state.StatusMessage.Contains("Downloaded", StringComparison.OrdinalIgnoreCase) ||
                                             state.StatusMessage.Contains("Cleared", StringComparison.OrdinalIgnoreCase) ||
                                             state.StatusMessage.Contains("Authorized", StringComparison.OrdinalIgnoreCase)
                ? Color.FromHex("#66EEAA")
                : Color.FromHex("#FFD166");
            _statusLabel.Visible = true;
        }
        else
        {
            _statusLabel.Visible = false;
        }

        // Update Authorization Panel
        _authStatusLabel.Text = state.IsAuthorized
            ? $"AUTH: {(string.IsNullOrWhiteSpace(state.AuthorizedByLabel) ? "UNLOCKED" : state.AuthorizedByLabel)}"
            : "AUTH: LOCKED (Swipe ID or .acc)";
        _authStatusLabel.ToolTip = _authStatusLabel.Text;
        _authStatusLabel.FontColorOverride = state.IsAuthorized
            ? Color.FromHex("#66EEAA")
            : Color.FromHex("#FFD166");

        _authButtonsWrap.RemoveAllChildren();
        foreach (var accPath in state.AvailableAccFiles)
        {
            var pathCopy = accPath;
            var shortName = pathCopy.Contains('/') ? pathCopy[(pathCopy.LastIndexOf('/') + 1)..] : pathCopy;
            var accBtn = new ContentButton
            {
                Name = $"CameraAuthAccBtn_{shortName}",
                Text = $"Auth ({shortName})",
                MinHeight = 22f
            };
            accBtn.Label.Margin = new Thickness(5, 2);
            accBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.AuthorizeWithAcc, zone: pathCopy));
            _authButtonsWrap.AddChild(accBtn);
        }

        if (state.AvailableAccFiles.Count > 0 && !state.IsAuthorized)
        {
            var quickBtn = new ContentButton
            {
                Name = "CameraQuickAuthAccButton",
                Text = "Auth .acc",
                MinHeight = 22f
            };
            quickBtn.Label.Margin = new Thickness(5, 2);
            quickBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(
                SurveillanceCameraAction.AuthorizeWithAcc,
                zone: state.SelectedAccFilePath ?? state.AvailableAccFiles[0]));
            _authButtonsWrap.AddChild(quickBtn);
        }

        if (state.IsAuthorized)
        {
            var lockBtn = new ContentButton
            {
                Name = "CameraRevokeAuthButton",
                Text = "Lock",
                MinHeight = 22f
            };
            lockBtn.Label.Margin = new Thickness(5, 2);
            lockBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.RevokeAuth));
            _authButtonsWrap.AddChild(lockBtn);
        }

        // Rebuild zone buttons
        _zoneBar.RemoveAllChildren();
        foreach (var zone in state.Zones)
        {
            var captured = zone;
            var zBtn = new ContentButton
            {
                Text = zone,
                MinWidth = 48,
                MinHeight = 26f,
                Pressed = string.Equals(zone, state.SelectedZone, StringComparison.OrdinalIgnoreCase)
            };
            zBtn.Label.Margin = new Thickness(7, 3);
            zBtn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.SelectZone, zone: captured));
            _zoneBar.AddChild(zBtn);
        }

        RebuildCameraList();
        SyncActiveCameraFeeds(state);
        UpdateLogsAndAudio(state);
        UpdateLogsToggleButtonText(state);
    }

    private static List<ActiveCameraFeedDto> GetEffectiveFeeds(SurveillanceCameraUiState state)
    {
        if (state.ActiveFeeds.Count > 0)
            return state.ActiveFeeds;

        if (state.ActiveCamera is { } camNet)
        {
            return new List<ActiveCameraFeedDto>
            {
                new(
                    camNet,
                    state.ActiveCameraGuid,
                    state.ActiveCameraName,
                    state.ActiveCameraZone,
                    state.ActiveHasMicrophone,
                    state.ActiveLogs.Count,
                    state.ActiveLogUsedBytes,
                    state.ActiveLogMaxBytes,
                    state.ActiveDroppedLogs,
                    state.LiveAudioFeed)
            };
        }

        return new List<ActiveCameraFeedDto>();
    }

    private void ToggleLogsTab()
    {
        _logsExpanded = !_logsExpanded;
        _inspectorDrawer.Visible = _logsExpanded;
        _feedGrid.Visible = !_logsExpanded;
        _logsToggleBtn.Pressed = _logsExpanded;

        if (!_logsExpanded)
        {
            foreach (var tile in _orderedTiles)
            {
                if (tile.Visible && tile.HasLiveEye)
                    tile.Viewport.RequestRender();
            }
        }

        if (_lastState != null)
            UpdateLogsToggleButtonText(_lastState);

        NotifyParentWindowFocusState();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void NotifyParentWindowFocusState()
    {
        for (var cur = Parent; cur != null; cur = cur.Parent)
        {
            if (cur is ComputerDisplayWindow win)
            {
                win.SetCameraFocusForced(ShouldForceViewportFocus);
                break;
            }
        }
    }

    private void UpdateLogsToggleButtonText(SurveillanceCameraUiState state)
    {
        if (_logsExpanded)
        {
            _logsToggleBtn.Text = "Feeds";
            _logsToggleBtn.ToolTip = "Return to live camera grid";
            return;
        }

        _logsToggleBtn.Text = state.ActiveLogs.Count > 0
            ? $"Logs ({state.ActiveLogs.Count})"
            : "Logs";
        _logsToggleBtn.ToolTip = "Open witness logs and live audio for the focused camera";
    }

    private void ToggleSoloForFocused()
    {
        if (_lastState == null)
            return;

        if (_soloCamera != null)
            _soloCamera = null;
        else if (_lastState.ActiveCamera is { } focused)
            _soloCamera = focused;

        if (_logsExpanded)
            ToggleLogsTab();

        SyncActiveCameraFeeds(_lastState);
    }

    private void ToggleSoloForCamera(NetEntity camNet)
    {
        if (_lastState == null)
            return;

        _soloCamera = _soloCamera == camNet ? null : camNet;
        if (_logsExpanded)
            ToggleLogsTab();

        SyncActiveCameraFeeds(_lastState);
    }

    private void SetMapMode(bool mapMode)
    {
        _mapMode = mapMode;
        _listModeBtn.Pressed = !mapMode;
        _mapModeBtn.Pressed = mapMode;
        _camScroll.Visible = !mapMode;
        _mapSelector.Visible = mapMode;
    }

    private void RebuildCameraList()
    {
        _cameraListContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        var activeSet = new HashSet<NetEntity>();
        foreach (var f in GetEffectiveFeeds(_lastState))
            activeSet.Add(f.CameraEntity);

        var q = (_searchEdit.Text ?? string.Empty).Trim();
        var shown = 0;
        var mapMarkers = new List<DeviceNavMapMarker>();
        foreach (var cam in _lastState.Cameras)
        {
            if (!string.Equals(_lastState.SelectedZone, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cam.Zone, _lastState.SelectedZone, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(q) &&
                !cam.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !cam.Zone.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !cam.CameraGuid.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var net = cam.CameraEntity;
            var isLive = activeSet.Contains(net);
            var isFocused = _lastState.ActiveCamera == net;
            var blipColor = isFocused
                ? Color.FromHex("#00FFCC")
                : isLive
                    ? Color.FromHex("#FBFF19")
                    : cam.Active
                        ? Color.FromHex("#CC55FF")
                        : Color.FromHex("#888888");
            mapMarkers.Add(new DeviceNavMapMarker(
                net,
                net.ToString(),
                cam.Name,
                cam.GridEntity,
                cam.LocalPosition,
                blipColor,
                Selected: isFocused || isLive,
                Active: cam.Active));

            var livePrefix = isLive ? "[LIVE] " : string.Empty;
            var micSuffix = cam.HasMicrophone ? " [MIC]" : string.Empty;
            var stateTag = cam.Active ? string.Empty : " [OFF]";
            var display = $"{livePrefix}{cam.Name}{micSuffix}{stateTag}";
            var btn = new ContentButton
            {
                Text = display,
                ToolTip = $"{cam.Name} [{cam.CameraGuid}] ({cam.Zone}){(cam.HasMicrophone ? " • Mic" : string.Empty)}{(isLive ? " • STREAMING" : string.Empty)}",
                HorizontalExpand = true,
                MinHeight = 28f,
                Disabled = !cam.Active,
                Pressed = isFocused || isLive
            };
            btn.Label.HorizontalAlignment = HAlignment.Stretch;
            btn.Label.TextAlignment = HAlignment.Left;
            btn.Label.ClipText = true;
            btn.Label.Margin = new Thickness(7, 4);
            btn.OnPressed += _ => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.SelectCamera, cameraEntity: net));
            _cameraListContainer.AddChild(btn);
        }

        if (shown == 0)
        {
            _cameraListContainer.AddChild(new ContentLabel
            {
                Text = _lastState.Online ? "No Wi-Fi cameras found in this zone." : "Router offline.",
                FontColorOverride = Color.FromHex("#8899AA"),
                HorizontalExpand = true
            });
        }

        _mapSelector.SetMarkers(
            mapMarkers,
            _lastState.ActiveCamera != null ? $"Focused Camera: {_lastState.ActiveCameraName}" : "Select a Wi-Fi Camera");
    }

    private void SyncActiveCameraFeeds(SurveillanceCameraUiState state)
    {
        var feeds = GetEffectiveFeeds(state);
        var desiredNets = new HashSet<NetEntity>();
        foreach (var feed in feeds)
            desiredNets.Add(feed.CameraEntity);

        if (_soloCamera is { } soloNet && !desiredNets.Contains(soloNet))
            _soloCamera = null;
        if (feeds.Count <= 1)
            _soloCamera = null;

        // Remove stale tiles
        var toRemove = new List<NetEntity>();
        foreach (var existingNet in _tilesByNet.Keys)
        {
            if (!desiredNets.Contains(existingNet))
                toRemove.Add(existingNet);
        }

        foreach (var net in toRemove)
        {
            if (_tilesByNet.TryGetValue(net, out var tile))
            {
                _feedGrid.RemoveTile(tile);
                _tilesByNet.Remove(net);
                _orderedTiles.Remove(tile);
            }
        }

        // Sync EyeLerpingSystem across all currently active camera entities
        var resolvedActiveUids = new HashSet<EntityUid>();
        foreach (var feed in feeds)
        {
            if (_entMan.TryGetEntity(feed.CameraEntity, out var uid) && _entMan.EntityExists(uid.Value))
                resolvedActiveUids.Add(uid.Value);
        }

        var eyeLerping = _entMan.System<EyeLerpingSystem>();
        var staleLerped = new List<EntityUid>();
        foreach (var uid in _lerpedCameraUids)
        {
            if (!resolvedActiveUids.Contains(uid))
                staleLerped.Add(uid);
        }

        foreach (var uid in staleLerped)
        {
            if (_entMan.EntityExists(uid))
                eyeLerping.RemoveEye(uid);
            _lerpedCameraUids.Remove(uid);
        }

        foreach (var uid in resolvedActiveUids)
        {
            if (_lerpedCameraUids.Add(uid))
                eyeLerping.AddEye(uid);
        }

        // Create/update tiles in server order (all feeds run at full 480x480 native resolution via OxydCameraViewport)
        var focusedNet = state.ActiveCamera ?? (feeds.Count > 0 ? feeds[0].CameraEntity : (NetEntity?) null);
        _orderedTiles.Clear();

        for (var i = 0; i < feeds.Count; i++)
        {
            var feed = feeds[i];
            if (!_tilesByNet.TryGetValue(feed.CameraEntity, out var tile))
            {
                var capturedNet = feed.CameraEntity;
                tile = new CameraFeedTile(
                    capturedNet,
                    _defaultEye,
                    CameraPovPixels,
                    onFocus: () => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.FocusCamera, cameraEntity: capturedNet)),
                    onToggleSolo: () => ToggleSoloForCamera(capturedNet),
                    onClose: () => SendProgramMessage(new SurveillanceCameraActionMessage(SurveillanceCameraAction.DisconnectCamera, cameraEntity: capturedNet)));
                _tilesByNet[feed.CameraEntity] = tile;
                _feedGrid.AddTile(tile);
            }

            _orderedTiles.Add(tile);
            var isFocused = focusedNet == feed.CameraEntity;
            var isSolo = _soloCamera == feed.CameraEntity;
            tile.Visible = _soloCamera == null || isSolo;

            IEye? resolvedEye = null;
            if (_entMan.TryGetEntity(feed.CameraEntity, out var camUid) &&
                _entMan.EntityExists(camUid.Value) &&
                _entMan.TryGetComponent<EyeComponent>(camUid.Value, out var eyeComp))
            {
                resolvedEye = eyeComp.Eye;
            }

            // Ensure the focused (or first) viewport always carries the canonical name for tests & tooling.
            tile.Viewport.Name = isFocused ? "OxydCameraViewport" : $"OxydCameraViewport_{feed.CameraEntity}";
            tile.UpdateFeed(feed, resolvedEye, isFocused, isSolo, feeds.Count > 1);
        }

        _feedGrid.UpdateEmptyState(feeds.Count == 0);

        // Update top focused camera bar
        var hasFocused = focusedNet != null && feeds.Count > 0;
        if (hasFocused)
        {
            var title = $"{state.ActiveCameraName} [{state.ActiveCameraGuid}] ({state.ActiveCameraZone})";
            _activeCamTitleLabel.Text = title;
            _activeCamTitleLabel.ToolTip = title;
        }
        else
        {
            _activeCamTitleLabel.Text = "No active camera feed";
            _activeCamTitleLabel.ToolTip = null;
        }

        _disconnectBtn.Disabled = feeds.Count == 0;
        _disconnectBtn.Text = feeds.Count > 1 ? $"Close All ({feeds.Count})" : "Disconnect";
        _downloadLogsBtn.Disabled = !hasFocused;
        _clearLogsBtn.Disabled = !hasFocused;
        _soloToggleBtn.Disabled = feeds.Count <= 1;
        _soloToggleBtn.Pressed = _soloCamera != null;
        _soloToggleBtn.Text = _soloCamera != null ? "Grid" : "Solo";

        if (hasFocused && state.ActiveHasMicrophone)
        {
            _audioBadgeLabel.Text = "AUDIO: ON";
            _audioBadgeLabel.FontColorOverride = Color.FromHex("#66EEAA");
            _audioBadgeStyle.BackgroundColor = Color.FromHex("#103326");
            _audioBadgeStyle.BorderColor = Color.FromHex("#2E996B");
        }
        else
        {
            _audioBadgeLabel.Text = "AUDIO: OFF";
            _audioBadgeLabel.FontColorOverride = Color.FromHex("#8899AA");
            _audioBadgeStyle.BackgroundColor = Color.FromHex("#1A242E");
            _audioBadgeStyle.BorderColor = Color.FromHex("#3A5266");
        }

        NotifyParentWindowFocusState();
    }

    private void UpdateLogsAndAudio(SurveillanceCameraUiState state)
    {
        // Live audio strip (for focused camera)
        _liveAudioContainer.RemoveAllChildren();
        if (state.ActiveCamera != null && state.ActiveHasMicrophone)
        {
            _liveAudioPanel.Visible = true;
            if (state.LiveAudioFeed.Count == 0)
            {
                _liveAudioContainer.AddChild(new ContentLabel
                {
                    Text = "[MIC LIVE] Listening for speech near focused camera...",
                    FontColorOverride = Color.FromHex("#66CCAA"),
                    HorizontalExpand = true,
                    WordWrap = false,
                    ClipText = true
                });
            }
            else
            {
                var start = Math.Max(0, state.LiveAudioFeed.Count - 4);
                for (var i = start; i < state.LiveAudioFeed.Count; i++)
                {
                    var line = state.LiveAudioFeed[i];
                    var ts = TimeSpan.FromSeconds(Math.Truncate(line.Timestamp.TotalSeconds)).ToString(@"hh\:mm\:ss");
                    var text = $"[{ts}] {line.Speaker}: \"{line.Message}\"";
                    _liveAudioContainer.AddChild(new ContentLabel
                    {
                        Text = text,
                        ToolTip = text,
                        FontColorOverride = Color.FromHex("#99FFDD"),
                        HorizontalExpand = true,
                        WordWrap = false,
                        ClipText = true
                    });
                }
            }
        }
        else
        {
            _liveAudioPanel.Visible = false;
        }

        // Witness logs
        var droppedStr = state.ActiveDroppedLogs > 0 ? $" | Evicted: {state.ActiveDroppedLogs}" : string.Empty;
        var logHdr = $"WITNESS LOGS — {state.ActiveCameraName} ({state.ActiveLogUsedBytes} / {state.ActiveLogMaxBytes} B{droppedStr})";
        _logHeaderLabel.Text = state.ActiveCamera != null ? logHdr : $"WITNESS LOGS ({state.ActiveLogUsedBytes} / {state.ActiveLogMaxBytes} B)";
        _logHeaderLabel.ToolTip = _logHeaderLabel.Text;

        _logsContainer.RemoveAllChildren();
        if (state.ActiveCamera == null)
        {
            _logsContainer.AddChild(new ContentLabel
            {
                Text = "Select a camera feed to inspect or download its witness logs.",
                FontColorOverride = Color.FromHex("#7790A0"),
                HorizontalExpand = true
            });
            return;
        }

        if (state.ActiveLogs.Count == 0)
        {
            _logsContainer.AddChild(new ContentLabel
            {
                Text = "No witnessed events stored in camera memory.",
                FontColorOverride = Color.FromHex("#8899AA"),
                HorizontalExpand = true
            });
            return;
        }

        foreach (var entry in state.ActiveLogs)
        {
            var ts = TimeSpan.FromSeconds(Math.Truncate(entry.Timestamp.TotalSeconds)).ToString(@"hh\:mm\:ss");
            var col = entry.Category switch
            {
                "COMBAT" or "WEAPON" => Color.FromHex("#FF7A7A"),
                "THROW" => Color.FromHex("#FFB366"),
                "ALERT" => Color.FromHex("#FFD166"),
                "AUDIO" => Color.FromHex("#88FFCC"),
                "SIGHT" => Color.FromHex("#77D8FF"),
                _ => Color.FromHex("#A8C4D4")
            };
            _logsContainer.AddChild(new ContentLabel
            {
                Text = $"[{ts}] [{entry.Category}] {entry.Message}",
                FontColorOverride = col,
                HorizontalExpand = true,
                WordWrap = true
            });
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_lastState == null || _orderedTiles.Count == 0)
            return;

        // Retry resolving EyeComponent for any newly subscribed camera whose PVS entity just arrived.
        var needsRetry = false;
        var visibleLiveCount = 0;
        for (var i = 0; i < _orderedTiles.Count; i++)
        {
            var tile = _orderedTiles[i];
            if (!tile.Visible)
                continue;

            if (!tile.HasLiveEye)
                needsRetry = true;
            else
                visibleLiveCount++;
        }

        if (needsRetry)
            SyncActiveCameraFeeds(_lastState);

        // Skip world rendering completely while the Logs tab is covering the feed grid.
        if (!_feedGrid.Visible || visibleLiveCount == 0)
            return;

        // Guarantee every visible camera renders at >= 45 FPS across all display refresh rates.
        // On ~45-60 Hz displays (DeltaSeconds >= ~0.014s), render every visible feed each frame so
        // frame-pacing never skips alternate frames down to 30 FPS; on ultra-high-refresh displays
        // (120-240 Hz), preserve the accumulator remainder so every feed still renders at >= 45 FPS.
        for (var i = 0; i < _orderedTiles.Count; i++)
        {
            var tile = _orderedTiles[i];
            if (!tile.Visible || !tile.HasLiveEye)
                continue;

            tile.TimeSinceRender += args.DeltaSeconds;
            if (args.DeltaSeconds >= 0.014f || tile.TimeSinceRender >= MaxCameraRenderInterval)
            {
                tile.TimeSinceRender = tile.TimeSinceRender >= MaxCameraRenderInterval
                    ? tile.TimeSinceRender % MaxCameraRenderInterval
                    : 0f;
                tile.Viewport.RequestRender();
            }
        }
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        NotifyParentWindowFocusState();
    }

    protected override void ExitedTree()
    {
        if (_lerpedCameraUids.Count > 0)
        {
            var eyeLerping = _entMan.System<EyeLerpingSystem>();
            foreach (var uid in _lerpedCameraUids)
            {
                if (_entMan.EntityExists(uid))
                    eyeLerping.RemoveEye(uid);
            }
            _lerpedCameraUids.Clear();
        }

        for (var cur = Parent; cur != null; cur = cur.Parent)
        {
            if (cur is ComputerDisplayWindow win)
            {
                win.SetCameraFocusForced(false);
                break;
            }
        }

        base.ExitedTree();
    }

    /// <summary>
    /// Adaptive multi-camera feed grid (1..6 feeds in span-aware rows that fill 100% of the grid box).
    /// Measures to zero minimum so it never pushes surrounding controls or overlaps siblings.
    /// </summary>
    private sealed class CameraFeedGrid : ContentControl
    {
        private const float TileGap = 6f;

        private readonly List<CameraFeedTile> _tiles = new();
        private readonly ContentLabel _emptyLabel;
        private readonly ScalingViewport _standbyViewport;

        public CameraFeedGrid(FixedEye defaultEye, int povPixels)
        {
            RectClipContent = true;
            MouseFilter = MouseFilterMode.Pass;

            _standbyViewport = new ScalingViewport
            {
                Name = "OxydCameraViewport",
                ViewportSize = new Vector2i(povPixels, povPixels),
                RenderScaleMode = ScalingViewportRenderScaleMode.Fixed,
                FixedRenderScale = 1,
                StretchMode = ScalingViewportStretchMode.Bilinear,
                DeferRender = true,
                SkipSharpness = true,
                StretchToFill = true,
                MouseFilter = MouseFilterMode.Stop,
                Eye = defaultEye,
                Visible = false
            };
            AddChild(_standbyViewport);

            _emptyLabel = new ContentLabel
            {
                Name = "CameraNoFeedLabel",
                Text = "SELECT UP TO 6 WI-FI CAMERAS — Shift+Click or Right-Click inside any live feed to examine entities",
                FontColorOverride = Color.FromHex("#6B8FA3"),
                HorizontalAlignment = HAlignment.Stretch,
                TextAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                WordWrap = true
            };
            AddChild(_emptyLabel);
        }

        public void AddTile(CameraFeedTile tile)
        {
            _tiles.Add(tile);
            AddChild(tile);
            InvalidateArrange();
        }

        public void RemoveTile(CameraFeedTile tile)
        {
            _tiles.Remove(tile);
            RemoveChild(tile);
            InvalidateArrange();
        }

        public void UpdateEmptyState(bool isEmpty)
        {
            _emptyLabel.Visible = isEmpty;
            _standbyViewport.Name = isEmpty ? "OxydCameraViewport" : "OxydCameraViewportStandby";
            InvalidateArrange();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var child in Children)
                child.Measure(availableSize);

            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var visible = new List<CameraFeedTile>(_tiles.Count);
            foreach (var tile in _tiles)
            {
                if (tile.Visible)
                    visible.Add(tile);
            }

            if (visible.Count == 0)
            {
                _emptyLabel.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
                return finalSize;
            }

            var isSingleView = visible.Count == 1;
            for (var i = 0; i < visible.Count; i++)
                visible[i].IsSingleView = isSingleView;

            int rows;
            int maxCols;
            if (visible.Count == 1)
            {
                rows = 1;
                maxCols = 1;
            }
            else if (visible.Count == 2)
            {
                if (finalSize.X >= finalSize.Y * 0.9f)
                {
                    rows = 1;
                    maxCols = 2;
                }
                else
                {
                    rows = 2;
                    maxCols = 1;
                }
            }
            else if (visible.Count <= 4)
            {
                if (visible.Count == 3 && finalSize.X > finalSize.Y * 2.15f)
                {
                    rows = 1;
                    maxCols = 3;
                }
                else
                {
                    rows = 2;
                    maxCols = 2;
                }
            }
            else
            {
                if (finalSize.X >= finalSize.Y * 0.85f)
                {
                    rows = 2;
                    maxCols = 3;
                }
                else
                {
                    rows = 3;
                    maxCols = 2;
                }
            }

            var cellH = MathF.Max(46f, MathF.Floor((finalSize.Y - TileGap * (rows - 1)) / rows));
            var gridH = cellH * rows + TileGap * (rows - 1);
            var startY = MathF.Floor(MathF.Max(0f, (finalSize.Y - gridH) * 0.5f));

            var idx = 0;
            for (var r = 0; r < rows && idx < visible.Count; r++)
            {
                var remainingRows = rows - r;
                var remainingTiles = visible.Count - idx;
                var rowCols = remainingRows == 1
                    ? remainingTiles
                    : Math.Min(maxCols, (int) MathF.Ceiling(remainingTiles / (float) remainingRows));

                var cellW = MathF.Max(20f, MathF.Floor((finalSize.X - TileGap * (rowCols - 1)) / rowCols));
                var rowW = cellW * rowCols + TileGap * (rowCols - 1);
                var startX = MathF.Floor(MathF.Max(0f, (finalSize.X - rowW) * 0.5f));
                var y = startY + r * (cellH + TileGap);

                for (var c = 0; c < rowCols && idx < visible.Count; c++, idx++)
                {
                    var x = startX + c * (cellW + TileGap);
                    visible[idx].Arrange(UIBox2.FromDimensions(x, y, cellW, cellH));
                }
            }

            return finalSize;
        }
    }

    /// <summary>
    /// Single live camera tile that fills its rectangular grid cell and dynamically adapts its
    /// <see cref="ScalingViewport.ViewportSize"/> resolution to match the cell's aspect ratio
    /// (keeping the major axis at full <c>480</c> resolution while scaling the minor axis cleanly).
    /// </summary>
    private sealed class CameraFeedTile : ContentControl
    {
        private const float HeaderHeight = 28f;
        private const int MinAdaptivePovPixels = 192;
        private const int ViewportSizeSnapStep = 16;

        private readonly FixedEye _defaultEye;
        private readonly int _maxPovPixels;
        private readonly StyleBoxFlat _headerStyle;
        private readonly ContentPanelContainer _headerPanel;
        private readonly ContentButton _titleBtn;
        private readonly ContentLabel _micTag;
        private readonly ContentButton _soloBtn;
        private readonly ContentButton _closeBtn;

        private readonly StyleBoxFlat _frameStyle;
        private readonly ContentPanelContainer _viewportFrame;
        private readonly ContentLabel _syncLabel;

        public ScalingViewport Viewport { get; }
        public bool HasLiveEye { get; private set; }
        public bool IsSingleView { get; set; } = true;
        public float TimeSinceRender { get; set; }

        public CameraFeedTile(
            NetEntity camNet,
            FixedEye defaultEye,
            int povPixels,
            Action onFocus,
            Action onToggleSolo,
            Action onClose)
        {
            Name = $"CameraFeedTile_{camNet}";
            _defaultEye = defaultEye;
            _maxPovPixels = povPixels;
            RectClipContent = true;
            MouseFilter = MouseFilterMode.Pass;

            _headerStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1D29"),
                BorderColor = Color.FromHex("#254A60"),
                BorderThickness = new Thickness(1, 1, 1, 0),
                ContentMarginLeftOverride = 3,
                ContentMarginRightOverride = 3,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2
            };
            _headerPanel = new ContentPanelContainer
            {
                Name = "CameraTileHeader",
                RectClipContent = true,
                PanelOverride = _headerStyle
            };

            var headerRow = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                VerticalExpand = true,
                RectClipContent = true,
                SeparationOverride = 3
            };

            _titleBtn = new ContentButton
            {
                Name = "CameraTileFocusBtn",
                Text = "Camera",
                HorizontalExpand = true,
                MinWidth = 0,
                MinHeight = 22f
            };
            _titleBtn.Label.HorizontalAlignment = HAlignment.Stretch;
            _titleBtn.Label.TextAlignment = HAlignment.Left;
            _titleBtn.Label.ClipText = true;
            _titleBtn.Label.Margin = new Thickness(5, 2);
            _titleBtn.OnPressed += _ => onFocus();
            headerRow.AddChild(_titleBtn);

            _micTag = new ContentLabel
            {
                Text = "[M]",
                ToolTip = "Audio microphone module installed",
                WordWrap = false,
                FontColorOverride = Color.FromHex("#66EEAA"),
                Visible = false
            };
            headerRow.AddChild(_micTag);

            _soloBtn = new ContentButton
            {
                Name = "CameraTileSoloBtn",
                Text = "1x",
                ToolTip = "Toggle Solo / Grid view",
                MinWidth = 28,
                MaxWidth = 38,
                MinHeight = 22f,
                Visible = false
            };
            _soloBtn.Label.Margin = new Thickness(3, 2);
            _soloBtn.OnPressed += _ => onToggleSolo();
            headerRow.AddChild(_soloBtn);

            _closeBtn = new ContentButton
            {
                Name = "CameraTileCloseBtn",
                Text = "x",
                ToolTip = "Disconnect this camera",
                MinWidth = 22,
                MaxWidth = 24,
                MinHeight = 22f
            };
            _closeBtn.Label.Margin = new Thickness(3, 2);
            _closeBtn.OnPressed += _ => onClose();
            headerRow.AddChild(_closeBtn);

            _headerPanel.AddChild(headerRow);
            AddChild(_headerPanel);

            _frameStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#05090E"),
                BorderColor = Color.FromHex("#254A60"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 1,
                ContentMarginRightOverride = 1,
                ContentMarginTopOverride = 1,
                ContentMarginBottomOverride = 1
            };
            _viewportFrame = new ContentPanelContainer
            {
                Name = "CameraViewportFrame",
                RectClipContent = true,
                PanelOverride = _frameStyle
            };

            Viewport = new ScalingViewport
            {
                ViewportSize = new Vector2i(povPixels, povPixels),
                RenderScaleMode = ScalingViewportRenderScaleMode.Fixed,
                FixedRenderScale = 1,
                StretchMode = ScalingViewportStretchMode.Bilinear,
                DeferRender = true,
                SkipSharpness = true,
                StretchToFill = true,
                HorizontalExpand = true,
                VerticalExpand = true,
                MouseFilter = MouseFilterMode.Stop,
                Eye = _defaultEye,
                Visible = false
            };
            _syncLabel = new ContentLabel
            {
                Text = "SYNCING FEED...",
                FontColorOverride = Color.FromHex("#6B8FA3"),
                HorizontalAlignment = HAlignment.Stretch,
                TextAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                WordWrap = false,
                ClipText = true
            };

            _viewportFrame.AddChild(Viewport);
            _viewportFrame.AddChild(_syncLabel);
            AddChild(_viewportFrame);
        }

        public void UpdateFeed(ActiveCameraFeedDto feed, IEye? eye, bool focused, bool solo, bool multiActive)
        {
            _titleBtn.Text = feed.Name;
            _titleBtn.ToolTip = $"{feed.Name} [{feed.CameraGuid}] ({feed.Zone}) — click to focus";
            _titleBtn.Pressed = focused;

            _micTag.Visible = feed.HasMicrophone;
            _soloBtn.Visible = multiActive;
            _soloBtn.Text = solo ? "Grid" : "1x";
            _soloBtn.Pressed = solo;

            var borderCol = focused ? Color.FromHex("#00D4FF") : Color.FromHex("#254A60");
            _headerStyle.BorderColor = borderCol;
            _headerStyle.BackgroundColor = focused ? Color.FromHex("#112838") : Color.FromHex("#0F1D29");
            _frameStyle.BorderColor = borderCol;

            if (eye != null)
            {
                var becameLive = !HasLiveEye;
                HasLiveEye = true;
                Viewport.Eye = eye;
                Viewport.Visible = true;
                _syncLabel.Visible = false;
                if (becameLive)
                    Viewport.RequestRender();
            }
            else
            {
                HasLiveEye = false;
                Viewport.Eye = _defaultEye;
                Viewport.Visible = false;
                _syncLabel.Visible = true;
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _headerPanel.Measure(availableSize);
            _viewportFrame.Measure(availableSize);
            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var headerH = MathF.Min(HeaderHeight, MathF.Max(0f, finalSize.Y));
            var availH = MathF.Max(0f, finalSize.Y - headerH);

            if (IsSingleView)
            {
                var maxVpSize = new Vector2i(_maxPovPixels, _maxPovPixels);
                if (Viewport.ViewportSize != maxVpSize)
                    Viewport.ViewportSize = maxVpSize;
                Viewport.StretchToFill = false;

                var frameSide = MathF.Floor(MathF.Max(0f, MathF.Min(finalSize.X, availH)));
                var totalH = headerH + frameSide;
                var x = MathF.Floor(MathF.Max(0f, (finalSize.X - frameSide) * 0.5f));
                var y = MathF.Floor(MathF.Max(0f, (finalSize.Y - totalH) * 0.5f));

                _headerPanel.Arrange(UIBox2.FromDimensions(x, y, frameSide, headerH));
                _viewportFrame.Arrange(UIBox2.FromDimensions(x, y + headerH, frameSide, frameSide));
                return finalSize;
            }

            Viewport.StretchToFill = true;
            var frameW = MathF.Floor(MathF.Max(0f, finalSize.X));
            var frameH = MathF.Floor(availH);

            var innerW = MathF.Max(1f, frameW - 2f);
            var innerH = MathF.Max(1f, frameH - 2f);
            if (innerW >= 32f && innerH >= 32f)
            {
                var targetVpSize = ComputeAdaptiveViewportSize(innerW, innerH, _maxPovPixels);
                var curVpSize = Viewport.ViewportSize;
                if (Math.Abs(targetVpSize.X - curVpSize.X) >= ViewportSizeSnapStep ||
                    Math.Abs(targetVpSize.Y - curVpSize.Y) >= ViewportSizeSnapStep)
                {
                    Viewport.ViewportSize = targetVpSize;
                }
            }

            _headerPanel.Arrange(UIBox2.FromDimensions(0f, 0f, frameW, headerH));
            _viewportFrame.Arrange(UIBox2.FromDimensions(0f, headerH, frameW, frameH));
            return finalSize;
        }

        private static Vector2i ComputeAdaptiveViewportSize(float width, float height, int maxPovPixels)
        {
            if (width >= height)
            {
                var scaledH = maxPovPixels * (height / MathF.Max(1f, width));
                var snappedH = ((int) MathF.Round(scaledH / ViewportSizeSnapStep)) * ViewportSizeSnapStep;
                return new Vector2i(maxPovPixels, Math.Clamp(snappedH, MinAdaptivePovPixels, maxPovPixels));
            }
            else
            {
                var scaledW = maxPovPixels * (width / MathF.Max(1f, height));
                var snappedW = ((int) MathF.Round(scaledW / ViewportSizeSnapStep)) * ViewportSizeSnapStep;
                return new Vector2i(Math.Clamp(snappedW, MinAdaptivePovPixels, maxPovPixels), maxPovPixels);
            }
        }
    }
}
