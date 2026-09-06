using Robust.Shared.Map.Components;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// Chunks that must rebuild rims when a tile changes: the tile's chunk, plus
/// a neighbour chunk only when the tile sits on that chunk's edge (including corners).
/// </summary>
public static class TileBorderChunks
{
    public static Vector2i ChunkIndex(Vector2i gridIndices, ushort chunkSize = MapGridComponent.DefaultChunkSize)
    {
        return new Vector2i(
            (int) Math.Floor(gridIndices.X / (double) chunkSize),
            (int) Math.Floor(gridIndices.Y / (double) chunkSize));
    }

    public static void AppendDirtyChunks(
        Vector2i gridIndices,
        ICollection<Vector2i> dest,
        ushort chunkSize = MapGridComponent.DefaultChunkSize)
    {
        AppendDirtyChunks(gridIndices, ChunkIndex(gridIndices, chunkSize), dest, chunkSize);
    }

    public static void AppendDirtyChunks(
        Vector2i gridIndices,
        Vector2i chunk,
        ICollection<Vector2i> dest,
        ushort chunkSize = MapGridComponent.DefaultChunkSize)
    {
        var local = gridIndices - chunk * chunkSize;
        dest.Add(chunk);

        if (local.X == 0)
            dest.Add(chunk + new Vector2i(-1, 0));
        else if (local.X == chunkSize - 1)
            dest.Add(chunk + new Vector2i(1, 0));

        if (local.Y == 0)
            dest.Add(chunk + new Vector2i(0, -1));
        else if (local.Y == chunkSize - 1)
            dest.Add(chunk + new Vector2i(0, 1));

        if (local.X == 0 && local.Y == 0)
            dest.Add(chunk + new Vector2i(-1, -1));
        else if (local.X == 0 && local.Y == chunkSize - 1)
            dest.Add(chunk + new Vector2i(-1, 1));
        else if (local.X == chunkSize - 1 && local.Y == 0)
            dest.Add(chunk + new Vector2i(1, -1));
        else if (local.X == chunkSize - 1 && local.Y == chunkSize - 1)
            dest.Add(chunk + new Vector2i(1, 1));
    }
}
