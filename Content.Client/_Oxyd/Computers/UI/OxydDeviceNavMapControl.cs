using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Maths;

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
/// Reusable NavMap-based interactive device picker shared across APC Control,
/// Atmos Control, and Surveillance Camera Control programs.
/// </summary>
public sealed class OxydDeviceNavMapControl : ContentControl
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

    private readonly NavMapControl _navMap;
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

        _hintLabel = new ContentLabel
        {
            Name = $"{namePrefix}NavMapHint",
            Text = "Click a station beacon on the map to switch target device.",
            FontColorOverride = Color.FromHex("#88D4EE"),
            WordWrap = true
        };
        root.AddChild(_hintLabel);

        var mapFrame = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 250,
            RectClipContent = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#09121B"),
                BorderColor = Color.FromHex("#24526E"),
                BorderThickness = new Thickness(1)
            }
        };

        _navMap = new NavMapControl
        {
            Name = $"{namePrefix}NavMap",
            HorizontalExpand = true,
            VerticalExpand = true,
            MinHeight = 248,
            TileColor = new Color(20, 44, 58),
            WallColor = new Color(72, 168, 212)
        };
        _navMap.TrackedEntitySelectedAction += OnTrackedEntitySelected;

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
            ? $"Station Map: {markers.Count} device(s) — click any beacon to select"
            : $"{selectedSummary} ({markers.Count} mapped — click beacon to switch)";

        _markersByEnt.Clear();
        _markersByKey.Clear();
        _navMap.TrackedEntities.Clear();

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
        }
    }

    /// <summary>
    /// Simulates selecting a mapped device marker by its key (used by tests & UI actions).
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
