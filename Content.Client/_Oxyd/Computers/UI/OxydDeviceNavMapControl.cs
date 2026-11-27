using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
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
public sealed class OxydFittedNavMap : NavMapControl
{
    private readonly List<DeviceNavMapMarker> _markers = new();
    private bool _needsAutoFit = true;
    private Vector2 _lastFitSize;

    public OxydFittedNavMap()
    {
        WorldMinRange = 6f;
        WorldMaxRange = 256f;
        WorldRange = 36f;
        ActualRadarRange = 36f;
        HorizontalExpand = true;
        VerticalExpand = true;
        SetSize = Vector2.Zero;
        MinSize = new Vector2(260f, 220f);
        RectClipContent = true;

        TileColor = new Color(20, 44, 58);
        WallColor = new Color(72, 168, 212);
    }

    public void UpdateMarkers(IReadOnlyList<DeviceNavMapMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
    }

    public void RequestAutoFit()
    {
        _needsAutoFit = true;
    }

    protected override void Resized()
    {
        base.Resized();
        if ((Size - _lastFitSize).LengthSquared() > 16f)
            _needsAutoFit = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_needsAutoFit && Size.X >= 80f && Size.Y >= 80f && MapUid != null)
        {
            if (TryApplyRectangularFit())
                _needsAutoFit = false;
        }
    }

    public bool TryApplyRectangularFit()
    {
        if (Size.X < 40f || Size.Y < 40f || MapUid == null)
            return false;

        var hasBounds = false;
        float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

        void ExpandPoint(Vector2 p)
        {
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

        // TilePolygons and TileRects are stored in Y-up world/grid space or Y-inverted space:
        // TilePolygons vertices are direct grid-local coordinates (Y up).
        foreach (var (verts, _) in TilePolygons)
        {
            foreach (var v in verts)
                ExpandPoint(v);
        }

        foreach (var (lt, rb) in TileRects)
        {
            ExpandPoint(new Vector2(lt.X, -lt.Y));
            ExpandPoint(new Vector2(rb.X, -rb.Y));
        }

        foreach (var m in _markers)
            ExpandPoint(m.LocalPosition);

        if (!hasBounds)
            return false;

        var width = MathF.Max(10f, (maxX - minX) + 6f);
        var height = MathF.Max(10f, (maxY - minY) + 6f);
        var stationCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        // MinimapScale = (UIDisplayRadius * UIScale) / WorldRange maps 1 grid meter to (UIDisplayRadius / WorldRange) DIPs.
        var usableW = MathF.Max(120f, Size.X * 0.90f);
        var usableH = MathF.Max(100f, Size.Y * 0.86f);
        var reqRangeX = width * UIDisplayRadius / usableW;
        var reqRangeY = height * UIDisplayRadius / usableH;
        var fitRange = Math.Clamp(MathF.Max(reqRangeX, reqRangeY), WorldMinRange, WorldMaxRange);

        WorldRange = fitRange;
        ActualRadarRange = fitRange;

        var physCenter = EntManager.TryGetComponent<PhysicsComponent>(MapUid, out var phys)
            ? phys.LocalCenter
            : Vector2.Zero;

        var scale = fitRange > 0f ? (UIDisplayRadius * UIScale) / fitRange : 1f;
        var visualCenter = PixelSize * 0.5f;
        var diffWorld = (visualCenter - MidPointVector) / scale;
        var fittedOffset = (stationCenter - physCenter) - new Vector2(diffWorld.X, -diffWorld.Y);

        Offset = fittedOffset;
        TargetOffset = fittedOffset;
        Recentering = false;
        _lastFitSize = Size;
        return true;
    }
}

/// <summary>
/// Reusable NavMap-based interactive device picker shared across APC Control,
/// Atmos Control, Surveillance Camera Control, and Crew Monitor programs.
/// </summary>
public sealed class OxydDeviceNavMapControl : ContentControl
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

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
            Text = "Click any beacon on the station map.",
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
            MinWidth = 76
        };
        fitBtn.OnPressed += _ => _navMap.RequestAutoFit();

        headerRow.AddChild(_hintLabel);
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

    public void SetMarkers(IReadOnlyList<DeviceNavMapMarker> markers, string selectedSummary)
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
