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

    private static readonly LitanyEffectKind[] ImplementedEffects =
    [
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
    ];

    [Test]
    public void FoundationPolicy_ContainsExactlyThePlannedEffects()
    {
        Assert.That(LitanyHandlerCatalog.Implemented, Is.EquivalentTo(ImplementedEffects));
        Assert.That(
            LitanyHandlerCatalog.Foundation,
            Is.EquivalentTo(FoundationEffects));
        Assert.That(
            LitanyHandlerCatalog.Foundation,
            Has.Count.EqualTo(LitanyHandlerCatalog.ExpectedFoundationEffectCount));
    }

    [Test]
    public void FullCatalogPolicy_HasSixtyEffectsAndThirtySevenGatedEntries()
    {
        var effectCount = Enum.GetValues<LitanyEffectKind>().Length;

        Assert.That(effectCount, Is.EqualTo(LitanyCatalogValidator.ExpectedLitanyCount));
        Assert.That(
            effectCount - LitanyHandlerCatalog.Foundation.Count,
            Is.EqualTo(LitanyCatalogValidator.ExpectedDependencyGatedLitanyCount));
    }

    [Test]
    public void PlannedFoundationEffectsAreNotRuntimeHandlers_ExceptPacketB()
    {
        foreach (var effect in FoundationEffects)
        {
            var expected = ImplementedEffects.Contains(effect);
            Assert.That(LitanyHandlerCatalog.HasHandler(effect), Is.EqualTo(expected), effect.ToString());
            Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(effect), Is.EqualTo(expected), effect.ToString());
        }
    }

    [Test]
    public void EnabledEffectWithoutRuntimeHandlerFailsClosed()
    {
        var errors = LitanyCatalogValidator.ValidateMissingHandler(
            "OxydLitanyEntreaty",
            LitanyEffectKind.Entreaty,
            isAvailable: true);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("without a registered runtime handler"));
    }

    [Test]
    public void PacketBImplementedEffectsAllowEnabledCatalogEntries()
    {
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.Relief), Is.True);
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.SoulHunger), Is.True);
        Assert.That(
            LitanyCatalogValidator.ValidateMissingHandler(
                "OxydLitanyRelief",
                LitanyEffectKind.Relief,
                isAvailable: true),
            Is.Empty);
    }
}
