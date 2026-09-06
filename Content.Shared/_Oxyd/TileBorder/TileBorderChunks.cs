using Robust.Shared.Map.Components;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// Pure math helpers for floor-rim rebuild scoping.
/// A tile's rim depends on its 8 neighbours, so a change rebuilds that tile and its neighbours.
/// Not used for DecalChunk / chunk-entity access — DecalSystem owns storage.
/// </summary>
public static class TileBorderChunks
{
    private static readonly Vector2i[] AffectedOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(0, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1),
    ];

    public static Vector2i ChunkIndex(Vector2i gridIndices, ushort chunkSize = MapGridComponent.DefaultChunkSize)
    {
        return new Vector2i(
            (int) Math.Floor(gridIndices.X / (double) chunkSize),
            (int) Math.Floor(gridIndices.Y / (double) chunkSize));
    }

    /// <summary>
    /// Appends <paramref name="center"/> and its 8 neighbours to <paramref name="dest"/>: the tiles
    /// whose rims can change when <paramref name="center"/> changes.
    /// </summary>
    public static void AppendAffectedTiles(Vector2i center, ICollection<Vector2i> dest)
    {
        foreach (var offset in AffectedOffsets)
        {
            dest.Add(center + offset);
        }
    }
}
