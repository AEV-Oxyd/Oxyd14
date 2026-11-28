using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.UI;

public sealed record DeviceNavMapMarker(
    NetEntity Entity,
    string Key,
    string Label,
    NetEntity? GridEntity,
    Vector2 LocalPosition,
    Color Color,
    bool Selected = false,
    bool Active = true,
    bool Blinks = false);

/// <summary>
/// NavMapControl subclass that automatically centers on the geometric bounding box
/// of a rectangular station and computes an aspect-fitted WorldRange so the whole
/// station fits neatly inside arbitrary container aspect ratios.
/// </summary>
public sealed class OxydFittedNavMap : NavMapControl, IInputRelayTarget
{
    public void RelayKeyBindDown(GUIBoundKeyEventArgs args) => KeyBindDown(args);
    public void RelayKeyBindUp(GUIBoundKeyEventArgs args) => KeyBindUp(args);
    public void RelayMouseMove(GUIMouseMoveEventArgs args) => MouseMove(args);
    public void RelayMouseWheel(GUIMouseWheelEventArgs args) => MouseWheel(args);
    public void RelayMouseEntered() => MouseEntered();
    public void RelayMouseExited() => MouseExited();
    public void RelayKeyHeld(GUIKeyEventArgs args) => KeyHeld(args);
    public void RelayTextEntered(GUITextEnteredEventArgs args) => TextEntered(args);
    public void RelayTextEditing(GUITextEditingEventArgs args) => TextEditing(args);
    public void RelayKeyboardFocusEntered() => KeyboardFocusEntered();
    public void RelayKeyboardFocusExited() => KeyboardFocusExited();
    public void RelayControlFocusExited() => ControlFocusExited();
    private readonly List<DeviceNavMapMarker> _markers = new();
    private Control? _navTopPanel;
    private bool _needsAutoFit = true;
    private bool _userAdjustedView;
    private bool _wasVisibleInTree;
    private bool _lastHadGeometry;
    private Vector2 _lastFitSize;
    private Vector2i _lastFitPixelSize;
    private Vector2 _lastBoundsMin;
    private Vector2 _lastBoundsMax;

    public OxydFittedNavMap()
    {
        WorldMinRange = 4f;
        WorldMaxRange = 640f;
        WorldRange = 36f;
        ActualRadarRange = 36f;
        HorizontalExpand = true;
        VerticalExpand = true;
        HorizontalAlignment = HAlignment.Stretch;
        VerticalAlignment = VAlignment.Stretch;
        SetSize = new Vector2(float.NaN, float.NaN);
        MinSize = new Vector2(180f, 160f);
        RectClipContent = true;

        TileColor = new Color(20, 44, 58);
        WallColor = new Color(72, 168, 212);

        // NavMapControl's internal topPanel hardcodes SetWidth = 650f and clips over small panels.
        // Hide it completely so the fitted map fills 100% of its parent frame edge-to-edge.
        if (ChildCount > 0 && GetChild(0) is BoxContainer topContainer)
        {
            topContainer.MouseFilter = MouseFilterMode.Ignore;
            if (topContainer.ChildCount > 0 && topContainer.GetChild(0) is Control panel)
            {
                _navTopPanel = panel;
                panel.Visible = false;
                panel.SetWidth = float.NaN;
                panel.MinSize = Vector2.Zero;
                panel.HorizontalExpand = true;
            }
        }
    }

    public bool BeaconsVisible
    {
        get => FindBeaconsCheckBox(_navTopPanel)?.Pressed ?? true;
        set
        {
            if (FindBeaconsCheckBox(_navTopPanel) is { } cb)
                cb.Pressed = value;
        }
    }

    private static CheckBox? FindBeaconsCheckBox(Control? root)
    {
        if (root == null)
            return null;

        if (root is CheckBox cb)
            return cb;

        for (var i = 0; i < root.ChildCount; i++)
        {
            if (FindBeaconsCheckBox(root.GetChild(i)) is { } found)
                return found;
        }

        return null;
    }

    public void UpdateMarkers(IReadOnlyList<DeviceNavMapMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        if (!_userAdjustedView)
            _needsAutoFit = true;
    }

    public void RequestAutoFit()
    {
        _userAdjustedView = false;
        _needsAutoFit = true;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        var prevOffset = Offset;
        base.MouseMove(args);
        if ((Offset - prevOffset).LengthSquared() > 0.0001f)
            _userAdjustedView = true;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (MathF.Abs(args.Delta.Y) > 0.001f)
            _userAdjustedView = true;
    }

    protected override void Resized()
    {
        base.Resized();
        if ((Size - _lastFitSize).LengthSquared() > 4f ||
            (PixelSize - _lastFitPixelSize).LengthSquared > 4)
        {
            _needsAutoFit = true;
        }
    }

    protected override void UpdateNavMap()
    {
        base.UpdateNavMap();

        if (_userAdjustedView)
            return;

        if (TryComputeStationBounds(out var min, out var max, out var hasTileGeometry))
        {
            if (hasTileGeometry != _lastHadGeometry ||
                (min - _lastBoundsMin).LengthSquared() > 1f ||
                (max - _lastBoundsMax).LengthSquared() > 1f)
            {
                _needsAutoFit = true;
            }
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var vis = VisibleInTree;
        if (vis && !_wasVisibleInTree && !_userAdjustedView)
            _needsAutoFit = true;
        _wasVisibleInTree = vis;

        if (!_userAdjustedView &&
            (Size - _lastFitSize).LengthSquared() > 4f &&
            Size.X >= 80f && Size.Y >= 80f)
        {
            _needsAutoFit = true;
        }

        if (_needsAutoFit && vis && Size.X >= 80f && Size.Y >= 80f && MapUid != null)
        {
            if (TryApplyRectangularFit())
                _needsAutoFit = false;
        }
    }

    private bool TryComputeStationBounds(out Vector2 min, out Vector2 max, out bool hasTileGeometry)
    {
        var hasBounds = false;
        hasTileGeometry = false;
        float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

        void ExpandPoint(Vector2 p)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
                return;

            if (!hasBounds)
            {
                minX = maxX = p.X;
                minY = maxY = p.Y;
                hasBounds = true;
                return;
            }

            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        foreach (var (verts, _) in TilePolygons)
        {
            hasTileGeometry = true;
            foreach (var v in verts)
                ExpandPoint(v);
        }

        foreach (var (lt, rb) in TileRects)
        {
            hasTileGeometry = true;
            ExpandPoint(new Vector2(lt.X, -lt.Y));
            ExpandPoint(new Vector2(rb.X, -rb.Y));
        }

        foreach (var (o, t) in TileLines)
        {
            hasTileGeometry = true;
            ExpandPoint(new Vector2(o.X, -o.Y));
            ExpandPoint(new Vector2(t.X, -t.Y));
        }

        foreach (var m in _markers)
            ExpandPoint(m.LocalPosition);

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return hasBounds;
    }

    public bool TryApplyRectangularFit()
    {
        if (Size.X < 40f || Size.Y < 40f || MapUid == null)
            return false;

        if (!TryComputeStationBounds(out var min, out var max, out var hasTileGeometry))
            return false;

        var topPanelH = _navTopPanel != null && _navTopPanel.Visible
            ? MathF.Max(28f, _navTopPanel.Size.Y)
            : 0f;
        var padX = MathF.Max(14f, Size.X * 0.05f);
        var padTop = topPanelH + MathF.Max(10f, Size.Y * 0.03f);
        var padBottom = MathF.Max(12f, Size.Y * 0.04f);

        var usableW = MathF.Max(60f, Size.X - padX * 2f);
        var usableH = MathF.Max(60f, Size.Y - padTop - padBottom);

        var width = MathF.Max(8f, (max.X - min.X) + 4f);
        var height = MathF.Max(8f, (max.Y - min.Y) + 4f);
        var stationCenter = (min + max) * 0.5f;

        var reqRangeX = width * UIDisplayRadius / usableW;
        var reqRangeY = height * UIDisplayRadius / usableH;
        var fitRange = Math.Clamp(MathF.Max(reqRangeX, reqRangeY), WorldMinRange, WorldMaxRange);

        WorldRange = fitRange;
        ActualRadarRange = fitRange;

        var physCenter = EntManager.TryGetComponent<PhysicsComponent>(MapUid, out var phys)
            ? phys.LocalCenter
            : Vector2.Zero;

        var scale = fitRange > 0f && ScaledMinimapRadius > 0
            ? ScaledMinimapRadius / fitRange
            : (fitRange > 0f ? (UIDisplayRadius * UIScale) / fitRange : 1f);
        var visualCenter = new Vector2(PixelSize.X * 0.5f, (padTop + usableH * 0.5f) * UIScale);
        var diffWorld = (visualCenter - MidPointVector) / scale;
        var fittedOffset = (stationCenter - physCenter) - new Vector2(diffWorld.X, -diffWorld.Y);

        Offset = fittedOffset;
        TargetOffset = fittedOffset;
        Recentering = false;
        _lastFitSize = Size;
        _lastFitPixelSize = PixelSize;
        _lastBoundsMin = min;
        _lastBoundsMax = max;
        _lastHadGeometry = hasTileGeometry;
        return hasTileGeometry;
    }
}

/// <summary>
/// Reusable NavMap-based interactive device picker shared across APC Control,
/// Atmos Control, Surveillance Camera Control, and Crew Monitor programs.
/// </summary>
public sealed class OxydDeviceNavMapControl : ContentControl
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly OxydFittedNavMap _navMap;
    private readonly ContentLabel _hintLabel;
    private readonly Texture _activeTex;
    private readonly Texture _inactiveTex;
    private readonly Texture _selectedTex;

    private readonly Dictionary<NetEntity, DeviceNavMapMarker> _markersByEnt = new();
    private readonly Dictionary<string, DeviceNavMapMarker> _markersByKey = new(StringComparer.OrdinalIgnoreCase);
    private EntityUid? _boundGridUid;

    public event Action<string>? MarkerSelected;

    public int MarkerCount => _markersByKey.Count;
    public IReadOnlyCollection<string> MarkerKeys => _markersByKey.Keys;
    public OxydFittedNavMap NavMap => _navMap;

    public void RequestFit() => _navMap.RequestAutoFit();

    public OxydDeviceNavMapControl(string namePrefix)
    {
        IoCManager.InjectDependencies(this);

        HorizontalExpand = true;
        VerticalExpand = true;

        _activeTex = _cache.GetTexture("/Textures/Interface/NavMap/beveled_triangle.png");
        _selectedTex = _cache.GetTexture("/Textures/Interface/NavMap/beveled_square.png");
        _inactiveTex = _cache.GetTexture("/Textures/Interface/NavMap/beveled_circle.png");

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4
        };

        var headerRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _hintLabel = new ContentLabel
        {
            Name = $"{namePrefix}NavMapHint",
            Text = "Station Map",
            FontColorOverride = Color.FromHex("#88D4EE"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ClipText = true
        };
        _navMap = new OxydFittedNavMap
        {
            Name = $"{namePrefix}NavMap"
        };
        _navMap.TrackedEntitySelectedAction += OnTrackedEntitySelected;

        var fitBtn = new ContentButton
        {
            Name = $"{namePrefix}FitMapButton",
            Text = "Fit Map",
            MinWidth = 72
        };
        fitBtn.OnPressed += _ => _navMap.RequestAutoFit();

        var beaconsBtn = new ContentButton
        {
            Name = $"{namePrefix}BeaconsButton",
            Text = "Beacons",
            MinWidth = 72,
            Pressed = _navMap.BeaconsVisible
        };
        beaconsBtn.OnPressed += _ =>
        {
            var next = !_navMap.BeaconsVisible;
            _navMap.BeaconsVisible = next;
            beaconsBtn.Pressed = next;
        };

        headerRow.AddChild(_hintLabel);
        headerRow.AddChild(beaconsBtn);
        headerRow.AddChild(fitBtn);
        root.AddChild(headerRow);

        var mapFrame = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 240,
            RectClipContent = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#09121B"),
                BorderColor = Color.FromHex("#24526E"),
                BorderThickness = new Thickness(1)
            }
        };

        mapFrame.AddChild(_navMap);
        root.AddChild(mapFrame);
        AddChild(root);
    }

    private void OnTrackedEntitySelected(NetEntity? entity)
    {
        if (entity == null || !entity.Value.IsValid())
            return;

        if (_markersByEnt.TryGetValue(entity.Value, out var marker))
            MarkerSelected?.Invoke(marker.Key);
    }

    public void SetMarkers(IReadOnlyList<DeviceNavMapMarker> markers, string selectedSummary, NetEntity? fallbackGrid = null)
    {
        _hintLabel.Text = string.IsNullOrWhiteSpace(selectedSummary)
            ? $"Station Map ({markers.Count} beacons)"
            : $"{selectedSummary} ({markers.Count} mapped)";

        _markersByEnt.Clear();
        _markersByKey.Clear();
        _navMap.TrackedEntities.Clear();
        _navMap.UpdateMarkers(markers);

        EntityUid? firstGrid = null;
        foreach (var m in markers)
        {
            _markersByKey[m.Key] = m;
            if (m.Entity.IsValid())
                _markersByEnt[m.Entity] = m;

            if (m.GridEntity is not { } gNet || !_entMan.TryGetEntity(gNet, out var gUid))
                continue;

            firstGrid ??= gUid;
            var coords = new EntityCoordinates(gUid.Value, m.LocalPosition);
            var tex = m.Selected ? _selectedTex : m.Active ? _activeTex : _inactiveTex;
            _navMap.TrackedEntities[m.Entity] = new NavMapBlip(
                coords,
                tex,
                m.Color,
                m.Blinks,
                selectable: true,
                scale: m.Selected ? 1.35f : 1.0f);
        }

        if (firstGrid == null &&
            fallbackGrid is { } fbNet &&
            _entMan.TryGetEntity(fbNet, out var fbUid) &&
            _entMan.EntityExists(fbUid))
        {
            firstGrid = fbUid;
        }

        if (firstGrid != _boundGridUid)
        {
            _boundGridUid = firstGrid;
            _navMap.MapUid = firstGrid;
            _navMap.ForceNavMapUpdate();
            _navMap.RequestAutoFit();
        }
    }

    /// <summary>
    /// Simulates selecting a mapped device marker by its key (used by tests &amp; UI actions).
    /// </summary>
    public bool TrySelectByKey(string key)
    {
        if (_markersByKey.TryGetValue(key, out var marker))
        {
            MarkerSelected?.Invoke(marker.Key);
            return true;
        }

        return false;
    }
}
