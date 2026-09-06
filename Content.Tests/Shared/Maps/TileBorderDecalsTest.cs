using Content.Shared._Oxyd.TileBorder;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Shared.Maps;

[TestFixture, TestOf(typeof(TileBorderDecals))]
[Parallelizable(ParallelScope.All)]
public sealed class TileBorderDecalsTest
{
    [Test]
    public void RsiStem_StripsPathAndExtension()
    {
        var path = new ResPath("/Textures/Oxyd/erisported/tiles_steel.rsi");
        Assert.That(TileBorderDecals.RsiStem(path), Is.EqualTo("tiles_steel"));
    }

    [Test]
    public void PrototypeId_UsesPrefixStemAndState()
    {
        var path = new ResPath("/Textures/Oxyd/erisported/tiles_steel.rsi");
        Assert.That(TileBorderDecals.PrototypeId(path, "n"), Is.EqualTo("TileBorder-tiles_steel-n"));
        Assert.That(TileBorderDecals.PrototypeId(path, "out-ne"), Is.EqualTo("TileBorder-tiles_steel-out-ne"));
    }

    [Test]
    public void SharedRsi_SharesPrototypeIds()
    {
        var hull = new ResPath("/Textures/Oxyd/erisported/hullcenter.rsi");
        Assert.That(TileBorderDecals.PrototypeId(hull, "n"), Is.EqualTo("TileBorder-hullcenter-n"));
    }

    [Test]
    public void IsGenerated_PrefixOnly()
    {
        Assert.That(TileBorderDecals.IsGenerated("TileBorder-tiles_steel-n"), Is.True);
        Assert.That(TileBorderDecals.IsGenerated("WoodTrimThinBox"), Is.False);
        Assert.That(TileBorderDecals.IsGenerated("tile-border-steel-n"), Is.False);
        Assert.That(TileBorderDecals.IsGenerated(""), Is.False);
    }
}
