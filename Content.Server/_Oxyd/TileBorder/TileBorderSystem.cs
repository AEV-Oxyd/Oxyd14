using System.Collections.Frozen;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Oxyd.TileBorder;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Server.Physics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.TileBorder;

/// <summary>
/// Server-authored floor rims stored as decals on grid <see cref="DecalChunkComponent"/> chunk entities.
/// Rebuilds only the changed tile and its 8 neighbours; whole chunks only on grid init/split/prototype reload.
/// Defers updates while explosions process tiles.
/// </summary>
public sealed partial class TileBorderSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private ChunkEntitySystem _chunks = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ExplosionSystem _explosions = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<DecalChunkComponent> _decalQuery = default!;

    private FrozenDictionary<int, ContentTileDefinition> _byTypeId = FrozenDictionary<int, ContentTileDefinition>.Empty;
    private FrozenDictionary<int, string> _groupByTypeId = FrozenDictionary<int, string>.Empty;
    private FrozenSet<string> _validProtos = FrozenSet<string>.Empty;

    private readonly HashSet<(EntityUid Grid, Vector2i Chunk)> _dirtyChunks = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _dirtyTiles = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _chunkDrain = new();
    private readonly List<(EntityUid Grid, Vector2i Tile)> _tileDrain = new();
    private readonly List<Vector2i> _affectedTiles = new(9);
    private readonly Dictionary<(EntityUid Grid, Vector2i Chunk), List<Vector2i>> _tilesByChunk = new();
    private readonly HashSet<Vector2> _stripCoords = new();
    private readonly List<DecalIndex> _strip = new();
    private readonly List<string> _layers = new(8);

    public override void Initialize()
    {
        base.Initialize();
        RebuildIndex();
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<PostGridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    public override void Update(float frameTime)
    {
        if (_dirtyChunks.Count == 0 && _dirtyTiles.Count == 0)
            return;

        // Explosions change tiles in large batches over several ticks; rebuild once when done.
        if (_explosions.IsProcessing)
            return;

        // Tile definitions may finish registering after this system initializes.
        RebuildIndex();

        _chunkDrain.Clear();
        _chunkDrain.AddRange(_dirtyChunks);
        _dirtyChunks.Clear();

        foreach (var (grid, chunk) in _chunkDrain)
        {
            if (!_gridQuery.TryComp(grid, out var gridComp))
                continue;

            RebuildChunk(grid, gridComp, chunk);
        }

        _tileDrain.Clear();
        _tileDrain.AddRange(_dirtyTiles);
        _dirtyTiles.Clear();

        foreach (var (grid, tile) in _tileDrain)
        {
            var chunk = TileBorderChunks.ChunkIndex(tile);
            if (!_tilesByChunk.TryGetValue((grid, chunk), out var tiles))
                _tilesByChunk[(grid, chunk)] = tiles = new List<Vector2i>(4);

            tiles.Add(tile);
        }

        foreach (var ((grid, chunk), tiles) in _tilesByChunk)
        {
            if (!_gridQuery.TryComp(grid, out var gridComp))
                continue;

            RebuildTiles(grid, gridComp, chunk, tiles);
        }

        _tilesByChunk.Clear();
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        DirtyGrid(ev.EntityUid, ev.Grid);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var grid = args.Entity.Owner;

        foreach (var change in args.Changes)
        {
            _affectedTiles.Clear();
            TileBorderChunks.AppendAffectedTiles(change.GridIndices, _affectedTiles);
            foreach (var tile in _affectedTiles)
            {
                _dirtyTiles.Add((grid, tile));
            }
        }
    }

    private void OnGridSplit(ref PostGridSplitEvent ev)
    {
        if (_gridQuery.TryComp(ev.OldGrid, out var oldGrid))
            DirtyGrid(ev.OldGrid, oldGrid);

        if (_gridQuery.TryComp(ev.Grid, out var grid))
            DirtyGrid(ev.Grid, grid);
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        _dirtyChunks.RemoveWhere(entry => entry.Grid == ev.EntityUid);
        _dirtyTiles.RemoveWhere(entry => entry.Grid == ev.EntityUid);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ContentTileDefinition>() && !args.WasModified<DecalPrototype>())
            return;

        RebuildIndex();

        var query = EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out var grid))
        {
            DirtyGrid(uid, grid);
        }
    }

    private void DirtyGrid(EntityUid grid, MapGridComponent gridComp)
    {
        foreach (var tile in _map.GetAllTiles(grid, gridComp))
        {
            _dirtyChunks.Add((grid, TileBorderChunks.ChunkIndex(tile.GridIndices)));
        }
    }

    private void RebuildIndex()
    {
        var byTypeId = new Dictionary<int, ContentTileDefinition>();
        var groupByTypeId = new Dictionary<int, string>();

        foreach (var def in _tiles)
        {
            if (def is not ContentTileDefinition content || content.BorderSprites == null)
                continue;

            byTypeId[def.TileId] = content;
            groupByTypeId[def.TileId] = TileBorderMask.ResolveGroup(content);
        }

        var validProtos = new HashSet<string>();
        foreach (var proto in _prototypes.EnumeratePrototypes<DecalPrototype>())
        {
            if (TileBorderDecals.IsGenerated(proto.ID))
                validProtos.Add(proto.ID);
        }

        _byTypeId = byTypeId.ToFrozenDictionary();
        _groupByTypeId = groupByTypeId.ToFrozenDictionary();
        _validProtos = validProtos.ToFrozenSet();
    }

    /// <summary>
    /// Rebuilds an entire chunk. Used for whole-grid rebuilds (grid init, split, prototype reload).
    /// </summary>
    private void RebuildChunk(EntityUid grid, MapGridComponent gridComp, Vector2i chunkIndices)
    {
        StripGenerated(grid, chunkIndices);

        var chunkSize = MapGridComponent.DefaultChunkSize;
        var origin = chunkIndices * chunkSize;

        for (var x = 0; x < chunkSize; x++)
        {
            for (var y = 0; y < chunkSize; y++)
            {
                EmitRims(grid, gridComp, origin + new Vector2i(x, y));
            }
        }
    }

    /// <summary>
    /// Rebuilds rims for specific tiles in a chunk.
    /// </summary>
    private void RebuildTiles(EntityUid grid, MapGridComponent gridComp, Vector2i chunkIndices, List<Vector2i> tiles)
    {
        StripGenerated(grid, chunkIndices, tiles);

        foreach (var pos in tiles)
        {
            EmitRims(grid, gridComp, pos);
        }
    }

    private void EmitRims(EntityUid grid, MapGridComponent gridComp, Vector2i pos)
    {
        if (!_map.TryGetTile(gridComp, pos, out var tile) || tile.IsEmpty)
            return;

        if (!_byTypeId.TryGetValue(tile.TypeId, out var def))
            return;

        if (!_groupByTypeId.TryGetValue(tile.TypeId, out var group))
            return;

        var mask = TileBorderMask.Compute(pos, group, neighbour =>
        {
            if (!_map.TryGetTile(gridComp, neighbour, out var other) || other.IsEmpty)
                return null;

            return _groupByTypeId.TryGetValue(other.TypeId, out var otherGroup) ? otherGroup : null;
        });

        if (TileBorderMask.IsInterior(mask))
            return;

        _layers.Clear();
        TileBorderMask.AppendLayers(mask, _layers);

        var coords = new EntityCoordinates(grid, new Vector2(pos.X, pos.Y));
        var rsi = def.BorderSprites!.Value;
        foreach (var state in _layers)
        {
            var id = TileBorderDecals.PrototypeId(rsi, state);
            if (!_validProtos.Contains(id))
                continue;

            _decals.TryAddDecal(id, coords, out _, zIndex: TileBorderDecals.ZIndex, cleanable: false);
        }
    }

    /// <summary>
    /// Removes all generated rim decals in a chunk (whole-chunk rebuilds).
    /// </summary>
    private void StripGenerated(EntityUid grid, Vector2i chunkIndices)
    {
        StripGenerated(grid, chunkIndices, null);
    }

    /// <summary>
    /// Removes generated rim decals at the given tiles (null = all in chunk).
    /// </summary>
    private void StripGenerated(EntityUid grid, Vector2i chunkIndices, List<Vector2i>? tiles)
    {
        if (!_chunks.TryGetChunk(grid, chunkIndices, out var chunkEnt) ||
            !_decalQuery.TryComp(chunkEnt.Value.Owner, out var decals))
        {
            return;
        }

        _stripCoords.Clear();
        if (tiles != null)
        {
            foreach (var pos in tiles)
            {
                _stripCoords.Add(new Vector2(pos.X, pos.Y));
            }
        }

        _strip.Clear();
        foreach (var (id, decal) in decals.Decals)
        {
            if (!TileBorderDecals.IsGenerated(decal.Id))
                continue;

            if (tiles != null && !_stripCoords.Contains(decal.Coordinates))
                continue;

            _strip.Add(new DecalIndex(chunkEnt.Value.Comp.Chunk, id));
        }

        foreach (var index in _strip)
        {
            _decals.RemoveDecal(grid, index);
        }
    }
}
