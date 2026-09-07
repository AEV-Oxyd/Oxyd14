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
    public void Interior_IsFullMask()
    {
        var origin = new Vector2i(5, 5);
        var mask = TileBorderMask.Compute(origin, "steel", pos => "steel");
        Assert.That(mask, Is.EqualTo(TileBorderMask.InteriorMask));
        Assert.That(TileBorderMask.IsInterior(mask), Is.True);
    }

    [Test]
    public void Isolated_ZeroMask()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "steel", _ => null);
        Assert.That(mask, Is.EqualTo(0));
        Assert.That(TileBorderMask.IsInterior(mask), Is.False);
    }

    [Test]
    public void SharedGroup_DifferentTypeIdsStillInterior()
    {
        var origin = new Vector2i(0, 0);
        var mask = TileBorderMask.Compute(origin, "Plating", _ => "Plating");
        Assert.That(TileBorderMask.IsInterior(mask), Is.True);
    }

    [Test]
    public void EmptyNeighbour_CountsAsOutside()
    {
        var origin = new Vector2i(0, 0);
        string? GroupAt(Vector2i pos)
        {
            if (pos == origin + Direction.East.ToIntVec())
                return null;
            return "steel";
        }

        var mask = TileBorderMask.Compute(origin, "steel", GroupAt);
        Assert.That(mask & (1 << (int) Direction.East), Is.EqualTo(0));
        Assert.That(mask & (1 << (int) Direction.North), Is.Not.EqualTo(0));
    }

    [Test]
    public void RotateClockwise_ShiftsBitsByTwo()
    {
        // Bit 0 (S) -> bit 6 (W); bit 2 (E) -> bit 0 (S); bit 4 (N) -> bit 2 (E)
        byte mask = 1 << (int) Direction.South;
        Assert.That(TileBorderMask.RotateClockwise(mask), Is.EqualTo((byte) (1 << (int) Direction.West)));

        mask = 1 << (int) Direction.East;
        Assert.That(TileBorderMask.RotateClockwise(mask), Is.EqualTo((byte) (1 << (int) Direction.South)));

        mask = 1 << (int) Direction.North;
        Assert.That(TileBorderMask.RotateClockwise(mask), Is.EqualTo((byte) (1 << (int) Direction.East)));
    }

    [Test]
    public void RotateClockwise_FourTurnsIdentity()
    {
        for (var i = 0; i < 256; i++)
        {
            var m = (byte) i;
            var r = TileBorderMask.RotateClockwise(
                TileBorderMask.RotateClockwise(
                    TileBorderMask.RotateClockwise(
                        TileBorderMask.RotateClockwise(m))));
            Assert.That(r, Is.EqualTo(m), $"mask 0x{i:x2}");
        }
    }

    [Test]
    public void Canonicalize_PicksNumericallySmallestRotation()
    {
        // 0xC7 -> rotations 0xC7, 0xF1, 0x7C, 0x1F → canonical 0x1F at 3 turns
        var canonical = TileBorderMask.Canonicalize(0xC7, out var turns);
        Assert.That(canonical, Is.EqualTo(0x1F));
        Assert.That(turns, Is.EqualTo(3));
    }

    [Test]
    public void Canonicalize_ZeroIsIdentity()
    {
        var canonical = TileBorderMask.Canonicalize(0, out var turns);
        Assert.That(canonical, Is.EqualTo(0));
        Assert.That(turns, Is.EqualTo(0));
    }

    [Test]
    public void Canonicalize_AlreadyMinimal_ZeroTurns()
    {
        // 0x1F is already the smallest in its orbit
        var canonical = TileBorderMask.Canonicalize(0x1F, out var turns);
        Assert.That(canonical, Is.EqualTo(0x1F));
        Assert.That(turns, Is.EqualTo(0));
    }

    [Test]
    public void Canonicalize_ApplyingTurnsRecoversCanonical()
    {
        for (var i = 0; i < 256; i++)
        {
            var mask = (byte) i;
            var canonical = TileBorderMask.Canonicalize(mask, out var turns);
            var rotated = mask;
            for (var t = 0; t < turns; t++)
                rotated = TileBorderMask.RotateClockwise(rotated);
            Assert.That(rotated, Is.EqualTo(canonical), $"mask 0x{i:x2}");
        }
    }
}
