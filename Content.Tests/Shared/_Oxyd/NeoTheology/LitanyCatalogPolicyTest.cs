using System;
using Content.Shared._Oxyd.NeoTheology;
using NUnit.Framework;

namespace Content.Tests.Shared._Oxyd.NeoTheology;

[TestFixture]
[TestOf(typeof(LitanyHandlerCatalog))]
public sealed class LitanyCatalogPolicyTest
{
    private static readonly LitanyEffectKind[] FoundationEffects =
    [
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.CruciformSense,
        LitanyEffectKind.Revelation,
        LitanyEffectKind.Commitment,
        LitanyEffectKind.Deprivation,
        LitanyEffectKind.HandOfMercy,
        LitanyEffectKind.AbsolutionOfWounds,
        LitanyEffectKind.Epiphany,
        LitanyEffectKind.GraceOfPerseverance,
        LitanyEffectKind.UpholdHolyWord,
        LitanyEffectKind.BaptismalRecord,
        LitanyEffectKind.DivineBlessing,
        LitanyEffectKind.Confirmation,
        LitanyEffectKind.Adoption,
        LitanyEffectKind.Ordination,
        LitanyEffectKind.Omission,
        LitanyEffectKind.Excommunication,
        LitanyEffectKind.Convalescence,
        LitanyEffectKind.Succour,
        LitanyEffectKind.Sending,
        LitanyEffectKind.ActivateDoor,
    ];

    [Test]
    public void FoundationPolicy_ContainsExactlyTheEnabledEffects()
    {
        Assert.That(LitanyHandlerCatalog.Implemented, Is.Empty);
        Assert.That(
            LitanyHandlerCatalog.PendingFoundation,
            Is.EquivalentTo(FoundationEffects));
        Assert.That(
            LitanyHandlerCatalog.PendingFoundation,
            Has.Count.EqualTo(LitanyHandlerCatalog.ExpectedFoundationEffectCount));
    }

    [Test]
    public void FullCatalogPolicy_HasSixtyEffectsAndThirtySevenGatedEntries()
    {
        var effectCount = Enum.GetValues<LitanyEffectKind>().Length;

        Assert.That(effectCount, Is.EqualTo(LitanyCatalogValidator.ExpectedLitanyCount));
        Assert.That(
            effectCount - LitanyHandlerCatalog.PendingFoundation.Count,
            Is.EqualTo(LitanyCatalogValidator.ExpectedDependencyGatedLitanyCount));
    }

    [Test]
    public void PendingFoundationEffectsHaveExactlyOneRegistration()
    {
        foreach (var effect in FoundationEffects)
            Assert.That(LitanyHandlerCatalog.HasExactlyOneRegistration(effect), Is.True, effect.ToString());
    }
}
