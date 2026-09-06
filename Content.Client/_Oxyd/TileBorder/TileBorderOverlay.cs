using System.Numerics;
using Content.Shared._Oxyd.TileBorder;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.TileBorder;

/// <summary>
/// Client-only same-group floor rims. Drawn under decals (ZIndex -1).
/// Per-chunk draw lists rebuild only when tiles in that chunk (or a neighbour chunk on the edge) change.
/// </summary>
public sealed class TileBorderOverlay : GridOverlay
{
    private readonly IResourceCache _resources;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;
    private readonly IEntityManager _entities;
    private readonly Dictionary<int, ContentTileDefinition> _byTypeId;
    private readonly Dictionary<int, string> _groupByTypeId;
    private readonly Dictionary<(ResPath Rsi, string State), Texture?> _textures = new();
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, ChunkCache>> _chunks = new();
    private readonly List<string> _layers = new(8);

    public TileBorderOverlay(
        IResourceCache resources,
        SharedMapSystem map,
        SharedTransformSystem xform,
        IEntityManager entities,
        Dictionary<int, ContentTileDefinition> byTypeId,
        Dictionary<int, string> groupByTypeId)
    {
        _resources = resources;
        _map = map;
        _xform = xform;
        _entities = entities;
        _byTypeId = byTypeId;
        _groupByTypeId = groupByTypeId;
        ZIndex = -1;
    }

    public void ClearAll()
    {
        _textures.Clear();
        _chunks.Clear();
    }

    public void DropGrid(EntityUid grid)
    {
        _chunks.Remove(grid);
    }

    public void InvalidateChunk(EntityUid grid, Vector2i chunk)
    {
        if (_chunks.TryGetValue(grid, out var byChunk) && byChunk.TryGetValue(chunk, out var cache))
            cache.Dirty = true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_byTypeId.Count == 0)
            return;

        var gridUid = Grid.Owner;
        var grid = Grid.Comp;

        if (!_entities.TryGetComponent(gridUid, out TransformComponent? xform))
            return;

        if (xform.MapID != args.MapId)
            return;

        var (_, _, worldMatrix) = _xform.GetWorldPositionRotationMatrix(xform);
        var localBounds = _xform.GetInvWorldMatrix(xform).TransformBox(args.WorldBounds.Enlarged(1f));
        var tileSize = (float) grid.TileSize;

        var x0 = (int) MathF.Floor(localBounds.Left / tileSize);
        var y0 = (int) MathF.Floor(localBounds.Bottom / tileSize);
        var x1 = (int) MathF.Ceiling(localBounds.Right / tileSize);
        var y1 = (int) MathF.Ceiling(localBounds.Top / tileSize);

        var handle = args.WorldHandle;
        handle.SetTransform(worldMatrix);

        var c0 = _map.GridTileToChunkIndices(grid, new Vector2i(x0, y0));
        var c1 = _map.GridTileToChunkIndices(grid, new Vector2i(x1 - 1, y1 - 1));
        if (!_chunks.TryGetValue(gridUid, out var byChunk))
            _chunks[gridUid] = byChunk = new Dictionary<Vector2i, ChunkCache>();

        for (var cx = c0.X; cx <= c1.X; cx++)
        {
            for (var cy = c0.Y; cy <= c1.Y; cy++)
            {
                var chunk = new Vector2i(cx, cy);
                if (!grid.HasChunk(chunk))
                    continue;

                if (!byChunk.TryGetValue(chunk, out var cache))
                    byChunk[chunk] = cache = new ChunkCache();

                if (cache.Dirty)
                    Rebuild(grid, chunk, cache);

                foreach (var (pos, texture) in cache.Draws)
                {
                    if (pos.X < x0 || pos.X >= x1 || pos.Y < y0 || pos.Y >= y1)
                        continue;

                    handle.DrawTexture(texture, new Vector2(pos.X * tileSize, pos.Y * tileSize));
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
        // Clyde only flushes WorldSpaceGrids overlays before the next grid
        // when this is set; otherwise shuttle/station rims can draw over
        // the other grid's tiles.
        RequiresFlush = true;
    }

    private void Rebuild(MapGridComponent grid, Vector2i chunk, ChunkCache cache)
    {
        cache.Draws.Clear();
        cache.Dirty = false;

        var chunkSize = MapGridComponent.DefaultChunkSize;
        var origin = chunk * chunkSize;

        for (var x = 0; x < chunkSize; x++)
        {
            for (var y = 0; y < chunkSize; y++)
            {
                var pos = origin + new Vector2i(x, y);
                if (!_map.TryGetTile(grid, pos, out var tile) || tile.IsEmpty)
                    continue;

                if (!_byTypeId.TryGetValue(tile.TypeId, out var def))
                    continue;

                if (!_groupByTypeId.TryGetValue(tile.TypeId, out var group))
                    continue;

                var mask = TileBorderMask.Compute(pos, group, neighbour =>
                {
                    if (!_map.TryGetTile(grid, neighbour, out var other) || other.IsEmpty)
                        return null;

                    return _groupByTypeId.TryGetValue(other.TypeId, out var otherGroup) ? otherGroup : null;
                });

                if (TileBorderMask.IsInterior(mask))
                    continue;

                _layers.Clear();
                TileBorderMask.AppendLayers(mask, _layers);
                var rsi = ToRsiPath(def.BorderSprites!.Value);
                foreach (var state in _layers)
                {
                    if (GetTexture(rsi, state) is not { } texture)
                        continue;

                    cache.Draws.Add((pos, texture));
                }
            }
        }
    }

    private Texture? GetTexture(ResPath rsi, string state)
    {
        var key = (rsi, state);
        if (_textures.TryGetValue(key, out var cached))
            return cached;

        Texture? texture = null;
        if (_resources.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / rsi, out var resource) &&
            resource.RSI.TryGetState(state, out var rsiState))
            texture = rsiState.Frame0;

        _textures[key] = texture;
        return texture;
    }

    private static ResPath ToRsiPath(ResPath path)
    {
        return path.TryRelativeTo(SpriteSpecifierSerializer.TextureRoot, out var rel) ? rel.Value : path;
    }

    private sealed class ChunkCache
    {
        public bool Dirty = true;
        public readonly List<(Vector2i Tile, Texture Texture)> Draws = new();
    }
}
