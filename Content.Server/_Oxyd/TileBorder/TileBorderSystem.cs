using System.Numerics;
using Content.Server.Decals;
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
/// Server-authored same-group floor rims stored as ordinary decals on the
/// existing <see cref="DecalChunkComponent"/> / <see cref="ChunkEntitySystem"/> chunk entity.
/// </summary>
public sealed partial class TileBorderSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private ChunkEntitySystem _chunks = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<DecalChunkComponent> _decalQuery = default!;

    private readonly Dictionary<int, ContentTileDefinition> _byTypeId = new();
    private readonly Dictionary<int, string> _groupByTypeId = new();
    private readonly HashSet<string> _validProtos = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Chunk)> _dirty = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _rebuild = new();
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
        if (_dirty.Count == 0)
            return;

        // Tile definitions may finish registering after this system initializes.
        RebuildIndex();

        _rebuild.Clear();
        _rebuild.AddRange(_dirty);
        _dirty.Clear();

        foreach (var (grid, chunk) in _rebuild)
        {
            if (!_gridQuery.TryComp(grid, out var gridComp))
                continue;

            RebuildChunk(grid, gridComp, chunk);
        }
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        DirtyGrid(ev.EntityUid, ev.Grid);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var grid = args.Entity.Owner;
        var chunkSize = MapGridComponent.DefaultChunkSize;

        foreach (var change in args.Changes)
        {
            _chunkScratch.Clear();
            TileBorderChunks.AppendDirtyChunks(change.GridIndices, change.ChunkIndex, _chunkScratch, chunkSize);
            foreach (var chunk in _chunkScratch)
            {
                _dirty.Add((grid, chunk));
            }
        }
    }

    private readonly List<Vector2i> _chunkScratch = new(4);

    private void OnGridSplit(ref PostGridSplitEvent ev)
    {
        if (_gridQuery.TryComp(ev.OldGrid, out var oldGrid))
            DirtyGrid(ev.OldGrid, oldGrid);

        if (_gridQuery.TryComp(ev.Grid, out var grid))
            DirtyGrid(ev.Grid, grid);
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        _dirty.RemoveWhere(entry => entry.Grid == ev.EntityUid);
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
            _dirty.Add((grid, TileBorderChunks.ChunkIndex(tile.GridIndices)));
        }
    }

    private void RebuildIndex()
    {
        _byTypeId.Clear();
        _groupByTypeId.Clear();
        _validProtos.Clear();

        foreach (var def in _tiles)
        {
            if (def is not ContentTileDefinition content || content.BorderSprites == null)
                continue;

            _byTypeId[def.TileId] = content;
            _groupByTypeId[def.TileId] = TileBorderMask.ResolveGroup(content);
        }

        foreach (var proto in _prototypes.EnumeratePrototypes<DecalPrototype>())
        {
            if (TileBorderDecals.IsGenerated(proto.ID))
                _validProtos.Add(proto.ID);
        }
    }

    private void RebuildChunk(EntityUid grid, MapGridComponent gridComp, Vector2i chunkIndices)
    {
        StripGenerated(grid, chunkIndices);

        var chunkSize = MapGridComponent.DefaultChunkSize;
        var origin = chunkIndices * chunkSize;

        for (var x = 0; x < chunkSize; x++)
        {
            for (var y = 0; y < chunkSize; y++)
            {
                var pos = origin + new Vector2i(x, y);
                if (!_map.TryGetTile(gridComp, pos, out var tile) || tile.IsEmpty)
                    continue;

                if (!_byTypeId.TryGetValue(tile.TypeId, out var def) || def.BorderSprites == null)
                    continue;

                if (!_groupByTypeId.TryGetValue(tile.TypeId, out var group))
                    continue;

                var mask = TileBorderMask.Compute(pos, group, neighbour =>
                {
                    if (!_map.TryGetTile(gridComp, neighbour, out var other) || other.IsEmpty)
                        return null;

                    return _groupByTypeId.TryGetValue(other.TypeId, out var otherGroup) ? otherGroup : null;
                });

                if (TileBorderMask.IsInterior(mask))
                    continue;

                _layers.Clear();
                TileBorderMask.AppendLayers(mask, _layers);

                var coords = new EntityCoordinates(grid, new Vector2(pos.X, pos.Y));
                var rsi = def.BorderSprites.Value;
                foreach (var state in _layers)
                {
                    var id = TileBorderDecals.PrototypeId(rsi, state);
                    if (!_validProtos.Contains(id))
                        continue;

                    _decals.TryAddDecal(id, coords, out _, zIndex: TileBorderDecals.ZIndex, cleanable: false);
                }
            }
        }
    }

    private void StripGenerated(EntityUid grid, Vector2i chunkIndices)
    {
        if (!_chunks.TryGetChunk(grid, chunkIndices, out var chunkEnt) ||
            !_decalQuery.TryComp(chunkEnt.Value.Owner, out var decals))
        {
            return;
        }

        _strip.Clear();
        foreach (var (id, decal) in decals.Decals)
        {
            if (TileBorderDecals.IsGenerated(decal.Id))
                _strip.Add(new DecalIndex(chunkEnt.Value.Comp.Chunk, id));
        }

        foreach (var index in _strip)
        {
            _decals.RemoveDecal(grid, index);
        }
    }
}
