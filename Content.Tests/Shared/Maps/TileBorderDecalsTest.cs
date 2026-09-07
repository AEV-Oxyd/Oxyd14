using System.Linq;
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
    public void StateName_IsLowercaseHex()
    {
        Assert.That(TileBorderDecals.StateName(0x0A), Is.EqualTo("0a"));
        Assert.That(TileBorderDecals.StateName(0x00), Is.EqualTo("00"));
        Assert.That(TileBorderDecals.StateName(0xBF), Is.EqualTo("bf"));
        Assert.That(TileBorderDecals.StateName(0xFE), Is.EqualTo("fe"));
    }

    [Test]
    public void PrototypeId_UsesPrefixStemAndLowercaseHex()
    {
        var path = new ResPath("/Textures/Oxyd/erisported/tiles_steel.rsi");
        Assert.That(TileBorderDecals.PrototypeId(path, 0x0A), Is.EqualTo("TileBorder-tiles_steel-0a"));
        Assert.That(TileBorderDecals.PrototypeId(path, 0x00), Is.EqualTo("TileBorder-tiles_steel-00"));
    }

    [Test]
    public void SharedRsi_SharesPrototypeIds()
    {
        var hull = new ResPath("/Textures/Oxyd/erisported/hullcenter.rsi");
        Assert.That(TileBorderDecals.PrototypeId(hull, 0x0A), Is.EqualTo("TileBorder-hullcenter-0a"));
    }

    [Test]
    public void AllCanonicalMasks_ExcludesInteriorAndHas69()
    {
        Assert.That(TileBorderDecals.AllCanonicalMasks, Does.Not.Contain(TileBorderMask.InteriorMask));
        Assert.That(TileBorderDecals.AllCanonicalMasks.Count, Is.EqualTo(69));
        Assert.That(TileBorderDecals.AllCanonicalMasks.Distinct().Count(), Is.EqualTo(69));
    }

    [Test]
    public void IsGenerated_PrefixOnly()
    {
        Assert.That(TileBorderDecals.IsGenerated("TileBorder-tiles_steel-0a"), Is.True);
        Assert.That(TileBorderDecals.IsGenerated("WoodTrimThinBox"), Is.False);
        Assert.That(TileBorderDecals.IsGenerated("tile-border-steel-0a"), Is.False);
        Assert.That(TileBorderDecals.IsGenerated(""), Is.False);
    }
}
