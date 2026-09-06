using Content.Shared._Oxyd.TileBorder;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.TileBorder;

/// <summary>
/// Client-only same-group floor rims. Registers the overlay and indexes
/// tiles that set <see cref="ContentTileDefinition.BorderSprites"/>.
/// </summary>
public sealed partial class TileBorderSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private readonly Dictionary<int, ContentTileDefinition> _byTypeId = new();
    private readonly Dictionary<int, string> _groupByTypeId = new();
    private TileBorderOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        RebuildIndex();
        _overlay = new TileBorderOverlay(_resources, _map, _xform, EntityManager, _byTypeId, _groupByTypeId);
        _overlays.AddOverlay(_overlay);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_overlay != null)
            _overlays.RemoveOverlay(_overlay);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ContentTileDefinition>())
            return;

        RebuildIndex();
        _overlay?.ClearAll();
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        if (_overlay == null)
            return;

        var chunkSize = MapGridComponent.DefaultChunkSize;
        var grid = args.Entity.Owner;

        foreach (var change in args.Changes)
        {
            var chunk = change.ChunkIndex;
            var local = change.GridIndices - chunk * chunkSize;
            _overlay.InvalidateChunk(grid, chunk);

            if (local.X == 0)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(-1, 0));
            else if (local.X == chunkSize - 1)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(1, 0));

            if (local.Y == 0)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(0, -1));
            else if (local.Y == chunkSize - 1)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(0, 1));

            if (local.X == 0 && local.Y == 0)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(-1, -1));
            else if (local.X == 0 && local.Y == chunkSize - 1)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(-1, 1));
            else if (local.X == chunkSize - 1 && local.Y == 0)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(1, -1));
            else if (local.X == chunkSize - 1 && local.Y == chunkSize - 1)
                _overlay.InvalidateChunk(grid, chunk + new Vector2i(1, 1));
        }
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        _overlay?.DropGrid(ev.EntityUid);
    }

    private void RebuildIndex()
    {
        _byTypeId.Clear();
        _groupByTypeId.Clear();
        foreach (var def in _tiles)
        {
            if (def is not ContentTileDefinition content || content.BorderSprites == null)
                continue;

            _byTypeId[def.TileId] = content;
            _groupByTypeId[def.TileId] = TileBorderMask.ResolveGroup(content);
        }
    }
}
