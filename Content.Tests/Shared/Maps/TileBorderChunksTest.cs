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
    public void InteriorTile_DirtiesOnlyOwnChunk()
    {
        var dirty = Dirty(new Vector2i(5, 5));
        Assert.That(dirty, Is.EqualTo(new[] { new Vector2i(0, 0) }));
    }

    [Test]
    public void WestEdge_DirtiesWestNeighbour()
    {
        var dirty = Dirty(new Vector2i(0, 5));
        Assert.That(dirty, Is.EquivalentTo(new[]
        {
            new Vector2i(0, 0),
            new Vector2i(-1, 0),
        }));
    }

    [Test]
    public void EastEdge_DirtiesEastNeighbour()
    {
        var dirty = Dirty(new Vector2i(15, 8));
        Assert.That(dirty, Is.EquivalentTo(new[]
        {
            new Vector2i(0, 0),
            new Vector2i(1, 0),
        }));
    }

    [Test]
    public void SouthWestCorner_DirtiesFourChunks()
    {
        var dirty = Dirty(new Vector2i(0, 0));
        Assert.That(dirty, Is.EquivalentTo(new[]
        {
            new Vector2i(0, 0),
            new Vector2i(-1, 0),
            new Vector2i(0, -1),
            new Vector2i(-1, -1),
        }));
    }

    [Test]
    public void NorthEastCorner_DirtiesPlusAxes()
    {
        var dirty = Dirty(new Vector2i(15, 15));
        Assert.That(dirty, Is.EquivalentTo(new[]
        {
            new Vector2i(0, 0),
            new Vector2i(1, 0),
            new Vector2i(0, 1),
            new Vector2i(1, 1),
        }));
    }

    [Test]
    public void NextChunkOrigin_IsOwnChunkNotPrevious()
    {
        var dirty = Dirty(new Vector2i(16, 16));
        Assert.That(dirty, Is.EquivalentTo(new[]
        {
            new Vector2i(1, 1),
            new Vector2i(0, 1),
            new Vector2i(1, 0),
            new Vector2i(0, 0),
        }));
    }

    [Test]
    public void NegativeTile_UsesFloorDivision()
    {
        Assert.That(TileBorderChunks.ChunkIndex(new Vector2i(-1, -1)), Is.EqualTo(new Vector2i(-1, -1)));
        var dirty = Dirty(new Vector2i(-1, -1));
        Assert.That(dirty, Does.Contain(new Vector2i(-1, -1)));
        Assert.That(dirty, Does.Contain(new Vector2i(0, 0)));
    }

    private static Vector2i[] Dirty(Vector2i tile)
    {
        var dest = new List<Vector2i>();
        TileBorderChunks.AppendDirtyChunks(tile, dest);
        return dest.Distinct().ToArray();
    }
}
