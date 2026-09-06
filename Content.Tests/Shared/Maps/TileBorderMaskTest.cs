using System.Collections.Generic;
using Content.Shared._Oxyd.TileBorder;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared.Maps;

[TestFixture, TestOf(typeof(TileBorderMask))]
[Parallelizable(ParallelScope.All)]
public sealed class TileBorderMaskTest
{
    [Test]
    public void ResolveGroup_DefaultsToId()
    {
        Assert.That(TileBorderMask.ResolveGroup("FloorSteel", null), Is.EqualTo("FloorSteel"));
        Assert.That(TileBorderMask.ResolveGroup("FloorSteel", ""), Is.EqualTo("FloorSteel"));
    }

    [Test]
    public void ResolveGroup_UsesExplicitGroup()
    {
        Assert.That(TileBorderMask.ResolveGroup("PlatingRCD", "Plating"), Is.EqualTo("Plating"));
    }

    [Test]
    public void Interior_SkipsAllLayers()
    {
        var origin = new Vector2i(5, 5);
        var mask = TileBorderMask.Compute(origin, "steel", pos => "steel");
        Assert.That(mask, Is.EqualTo(TileBorderMask.InteriorMask));
        Assert.That(TileBorderMask.IsInterior(mask), Is.True);
        Assert.That(Layers(mask), Is.Empty);
    }

    [Test]
    public void Isolated_FullOuterRim()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "steel", _ => null);
        Assert.That(mask, Is.EqualTo(0));
        Assert.That(Layers(mask), Is.EqualTo(new[]
        {
            TileBorderMask.North,
            TileBorderMask.South,
            TileBorderMask.East,
            TileBorderMask.West,
            TileBorderMask.OutNorthEast,
            TileBorderMask.OutNorthWest,
            TileBorderMask.OutSouthEast,
            TileBorderMask.OutSouthWest,
        }));
    }

    [Test]
    public void NorthEdge_OnlyNorthCardinal()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "steel", pos =>
            pos == origin + Direction.North.ToIntVec() ? "other" : "steel");
        Assert.That(Layers(mask), Is.EqualTo(new[] { TileBorderMask.North }));
    }

    [Test]
    public void InnerNorthEast_OnlyInnerCorner()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "steel", pos =>
            pos == origin + Direction.NorthEast.ToIntVec() ? "other" : "steel");
        Assert.That(Layers(mask), Is.EqualTo(new[] { TileBorderMask.InNorthEast }));
    }

    [Test]
    public void OuterNorthEast_CardinalsAndOuterCorner()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "steel", pos =>
        {
            var n = origin + Direction.North.ToIntVec();
            var e = origin + Direction.East.ToIntVec();
            return pos == n || pos == e ? "other" : "steel";
        });
        Assert.That(Layers(mask), Is.EqualTo(new[]
        {
            TileBorderMask.North,
            TileBorderMask.East,
            TileBorderMask.OutNorthEast,
        }));
    }

    [Test]
    public void SharedGroup_DifferentTypeIdsStillInterior()
    {
        var origin = new Vector2i(0, 0);
        // Neighbours report the same group string even if they came from another TypeId.
        var mask = TileBorderMask.Compute(origin, "Plating", _ => "Plating");
        Assert.That(TileBorderMask.IsInterior(mask), Is.True);
    }

    [Test]
    public void EmptyNeighbour_CountsAsOutside()
    {
        var origin = new Vector2i(0, 0);
        string GroupAt(Vector2i pos)
        {
            // East is empty (null); every other neighbour is in-group.
            if (pos == origin + Direction.East.ToIntVec())
                return null;
            return "steel";
        }

        var mask = TileBorderMask.Compute(origin, "steel", GroupAt);
        Assert.That(Layers(mask), Is.EqualTo(new[] { TileBorderMask.East }));
    }

    private static List<string> Layers(byte mask)
    {
        var states = new List<string>();
        TileBorderMask.AppendLayers(mask, states);
        return states;
    }
}
