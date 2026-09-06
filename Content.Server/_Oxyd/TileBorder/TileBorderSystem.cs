using System.Collections.Frozen;
using System.Numerics;
using Content.Server.Decals;
using Content.Shared._Oxyd.TileBorder;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.TileBorder;

/// <summary>
/// Server-authored floor rims: one precomposed decal per rim tile, oriented via Decal.Angle.
/// Trusts DecalSystem for storage, chunk ownership, and dirtying — no ChunkEntitySystem /
/// DecalChunkComponent access, no dirty-chunk drain, no ExplosionSystem deferral.
/// No RobustToolbox / engine changes.
/// </summary>
public sealed partial class TileBorderSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;

    private FrozenDictionary<int, ContentTileDefinition> _byTypeId = FrozenDictionary<int, ContentTileDefinition>.Empty;
    private FrozenDictionary<int, string> _groupByTypeId = FrozenDictionary<int, string>.Empty;
    private FrozenSet<string> _validProtos = FrozenSet<string>.Empty;

    private readonly List<Vector2i> _affectedTiles = new(9);
    private readonly List<DecalIndex> _strip = new();
    private static readonly Vector2 StripPad = new(0.01f);

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

    private void OnGridInit(GridInitializeEvent ev)
    {
        RebuildGrid(ev.EntityUid, ev.Grid);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var grid = args.Entity.Owner;
        if (!_gridQuery.TryComp(grid, out var gridComp))
            return;

        foreach (var change in args.Changes)
        {
            _affectedTiles.Clear();
            TileBorderChunks.AppendAffectedTiles(change.GridIndices, _affectedTiles);
            foreach (var tile in _affectedTiles)
            {
                StripGeneratedAt(grid, tile);
                EmitRims(grid, gridComp, tile);
            }
        }
    }

    private void OnGridSplit(ref PostGridSplitEvent ev)
    {
        if (_gridQuery.TryComp(ev.OldGrid, out var oldGrid))
            RebuildGrid(ev.OldGrid, oldGrid);

        if (_gridQuery.TryComp(ev.Grid, out var grid))
            RebuildGrid(ev.Grid, grid);
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        // No dirty sets to clear; DecalSystem owns decal storage on the removed grid.
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ContentTileDefinition>() && !args.WasModified<DecalPrototype>())
            return;

        RebuildIndex();

        var query = EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out var grid))
        {
            RebuildGrid(uid, grid);
        }
    }

    private void RebuildGrid(EntityUid grid, MapGridComponent gridComp)
    {
        foreach (var tile in _map.GetAllTiles(grid, gridComp))
        {
            StripGeneratedAt(grid, tile.GridIndices);
        }

        foreach (var tile in _map.GetAllTiles(grid, gridComp))
        {
            EmitRims(grid, gridComp, tile.GridIndices);
        }
    }

    /// <summary>
    /// RebuildIndex ONLY from Initialize and PrototypesReloaded — never from Update.
    /// </summary>
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

        var canonical = TileBorderMask.Canonicalize(mask, out var cwTurns);
        var id = TileBorderDecals.PrototypeId(def.BorderSprites!.Value, canonical);
        if (!_validProtos.Contains(id))
            return;

        var coords = new EntityCoordinates(grid, new Vector2(pos.X, pos.Y));
        _decals.TryAddDecal(
            id,
            coords,
            out _,
            rotation: Angle.FromDegrees(cwTurns * 90),
            zIndex: TileBorderDecals.ZIndex,
            cleanable: false);
    }

    private void StripGeneratedAt(EntityUid grid, Vector2i tile)
    {
        var origin = new Vector2(tile.X, tile.Y);
        var bounds = new Box2(origin - StripPad, origin + StripPad);

        _strip.Clear();
        foreach (var (index, decal) in _decals.GetDecalsIntersecting(grid, bounds))
        {
            if (TileBorderDecals.IsGenerated(decal.Id))
                _strip.Add(index);
        }

        foreach (var index in _strip)
        {
            _decals.RemoveDecal(grid, index);
        }
    }
}
