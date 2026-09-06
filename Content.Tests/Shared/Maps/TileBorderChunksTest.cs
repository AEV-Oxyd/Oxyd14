using System.Collections.Generic;
using System.Linq;
using Content.Shared._Oxyd.TileBorder;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared.Maps;

[TestFixture, TestOf(typeof(TileBorderChunks))]
[Parallelizable(ParallelScope.All)]
public sealed class TileBorderChunksTest
{
    [Test]
    public void AffectedTiles_AreCentreAndEightNeighbours()
    {
        var affected = Affected(new Vector2i(5, 5));
        Assert.That(affected, Is.EquivalentTo(new[]
        {
            new Vector2i(4, 4), new Vector2i(5, 4), new Vector2i(6, 4),
            new Vector2i(4, 5), new Vector2i(5, 5), new Vector2i(6, 5),
            new Vector2i(4, 6), new Vector2i(5, 6), new Vector2i(6, 6),
        }));
    }

    [Test]
    public void AffectedTiles_ChunkBoundaryCrossesIntoNeighbouringChunks()
    {
        // A change on chunk (1,1)'s south-west corner affects tiles in chunks (0,0), (0,1), (1,0) & (1,1)
        // without requiring any whole-chunk rebuild.
        var affected = Affected(new Vector2i(16, 16));
        Assert.That(affected, Is.EquivalentTo(new[]
        {
            new Vector2i(15, 15), new Vector2i(16, 15), new Vector2i(17, 15),
            new Vector2i(15, 16), new Vector2i(16, 16), new Vector2i(17, 16),
            new Vector2i(15, 17), new Vector2i(16, 17), new Vector2i(17, 17),
        }));

        Assert.That(affected.Select(pos => TileBorderChunks.ChunkIndex(pos)).Distinct(), Is.EquivalentTo(new[]
        {
            new Vector2i(0, 0),
            new Vector2i(0, 1),
            new Vector2i(1, 0),
            new Vector2i(1, 1),
        }));
    }

    [Test]
    public void AffectedTiles_NegativeTile_StaysNegative()
    {
        Assert.That(Affected(new Vector2i(-1, -1)), Is.EquivalentTo(new[]
        {
            new Vector2i(-2, -2), new Vector2i(-1, -2), new Vector2i(0, -2),
            new Vector2i(-2, -1), new Vector2i(-1, -1), new Vector2i(0, -1),
            new Vector2i(-2, 0), new Vector2i(-1, 0), new Vector2i(0, 0),
        }));
    }

    [Test]
    public void ChunkIndex_UsesFloorDivision()
    {
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(0, 0)), Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(15, 15)), Is.EqualTo(new Vector2i(0, 0)));
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(16, 16)), Is.EqualTo(new Vector2i(1, 1)));
    }

    [Test]
    public void ChunkIndex_NegativeTile_UsesFloorDivision()
    {
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(-1, -1)), Is.EqualTo(new Vector2i(-1, -1)));
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(-16, -16)), Is.EqualTo(new Vector2i(-1, -1)));
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(-17, -17)), Is.EqualTo(new Vector2i(-2, -2)));
    }

    private static Vector2i[] Affected(Vector2i tile)
    {
        var dest = new List<Vector2i>();
        TileBorderChunks.AppendAffectedTiles(tile, dest);
        return dest.Distinct().ToArray();
    }
}
