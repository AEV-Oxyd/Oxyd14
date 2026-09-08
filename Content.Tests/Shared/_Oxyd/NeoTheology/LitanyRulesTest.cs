using Content.Shared._Oxyd.NeoTheology;
using NUnit.Framework;

namespace Content.Tests.Shared._Oxyd.NeoTheology;

[TestFixture]
[TestOf(typeof(NeoTheologyHoliness))]
public sealed class LitanyRulesTest
{
    [Test]
    public void RankProfile_UsesFoundationCapsAndMultipliers()
    {
        Assert.That(NeoTheologyHoliness.RankCapacity(NeoTheologyRank.Disciple), Is.EqualTo(50d));
        Assert.That(NeoTheologyHoliness.RankCapacity(NeoTheologyRank.Preacher), Is.EqualTo(80d));
        Assert.That(NeoTheologyHoliness.RankCapacity(NeoTheologyRank.Inquisitor), Is.EqualTo(100d));

        Assert.That(NeoTheologyHoliness.RankRegenMultiplier(NeoTheologyRank.Disciple), Is.EqualTo(1d));
        Assert.That(NeoTheologyHoliness.RankRegenMultiplier(NeoTheologyRank.Preacher), Is.EqualTo(1.15d));
        Assert.That(NeoTheologyHoliness.RankRegenMultiplier(NeoTheologyRank.Inquisitor), Is.EqualTo(1.25d));
    }

    [Test]
    public void CognitionSteps_RoundsToNearestFourAndClampsNegativeValues()
    {
        Assert.That(NeoTheologyHoliness.CognitionSteps(-1), Is.EqualTo(0));
        Assert.That(NeoTheologyHoliness.CognitionSteps(0), Is.EqualTo(0));
        Assert.That(NeoTheologyHoliness.CognitionSteps(2), Is.EqualTo(1));
        Assert.That(NeoTheologyHoliness.CognitionSteps(6), Is.EqualTo(2));
    }

    [Test]
    public void RegenerationPerSecond_UsesElapsedBaseAndProfileFactors()
    {
        var baseRate = NeoTheologyHoliness.RegenerationPerSecond(
            NeoTheologyRank.Disciple,
            effectiveCog: 0,
            righteousLife: 0,
            channeling: false,
            eligibleDisciples: 0);
        Assert.That(baseRate, Is.EqualTo(1d / 60d).Within(1e-12));

        var boostedRate = NeoTheologyHoliness.RegenerationPerSecond(
            NeoTheologyRank.Preacher,
            effectiveCog: 4,
            righteousLife: 100,
            channeling: true,
            eligibleDisciples: 5);
        var expected = 1d / 60d * 1.15d * (1d + 0.05d + 1.5d + 1d);
        Assert.That(boostedRate, Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void CanAfford_RejectsInvalidCostsAndHonorsDebitTolerance()
    {
        Assert.That(NeoTheologyHoliness.CanAfford(10d, 10d), Is.True);
        Assert.That(NeoTheologyHoliness.CanAfford(10d, 10d + NeoTheologyHoliness.DebitTolerance / 2d), Is.True);
        Assert.That(NeoTheologyHoliness.CanAfford(10d, 11d), Is.False);
        Assert.That(NeoTheologyHoliness.CanAfford(double.NaN, 1d), Is.False);
        Assert.That(NeoTheologyHoliness.CanAfford(10d, -1d), Is.False);
        Assert.That(NeoTheologyHoliness.CanAfford(10d, double.NaN), Is.False);
    }

    [Test]
    public void ResourceHelpers_ClampAndNormalizeUnsafeValues()
    {
        Assert.That(NeoTheologyHoliness.ClampResource(-1d, 50d), Is.EqualTo(0d));
        Assert.That(NeoTheologyHoliness.ClampResource(75d, 50d), Is.EqualTo(50d));
        Assert.That(NeoTheologyHoliness.ClampResource(double.NaN, 50d), Is.EqualTo(0d));
        Assert.That(NeoTheologyHoliness.Normalize(NeoTheologyHoliness.DebitTolerance / 2d), Is.EqualTo(0d));
        Assert.That(NeoTheologyHoliness.Normalize(2d), Is.EqualTo(2d));
    }
}
