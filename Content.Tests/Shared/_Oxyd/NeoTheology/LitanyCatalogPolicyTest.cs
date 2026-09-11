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
        LitanyEffectKind.InstallUpgrade,
        LitanyEffectKind.UninstallUpgrade,
        LitanyEffectKind.Reincarnation,
        LitanyEffectKind.Resurrection,
        LitanyEffectKind.MakeCruciform,
        LitanyEffectKind.RepairDoor,
        LitanyEffectKind.PowerBiogenerator,
        LitanyEffectKind.BioreactorSolution,
        LitanyEffectKind.BioreactorChamber,
        LitanyEffectKind.Scrying,
        LitanyEffectKind.DivineIntervention,
        LitanyEffectKind.HolyGuidance,
        LitanyEffectKind.OrderArmaments,
    ];

    private static readonly LitanyEffectKind[] ImplementedEffects =
    [
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.CruciformSense,
        LitanyEffectKind.ActivateDoor,
        LitanyEffectKind.HandOfMercy,
        LitanyEffectKind.AbsolutionOfWounds,
        LitanyEffectKind.Convalescence,
        LitanyEffectKind.Succour,
        LitanyEffectKind.GraceOfPerseverance,
        LitanyEffectKind.UpholdHolyWord,
        LitanyEffectKind.Revelation,
        LitanyEffectKind.Epiphany,
        LitanyEffectKind.DivineBlessing,
        LitanyEffectKind.Commitment,
        LitanyEffectKind.Deprivation,
        LitanyEffectKind.Confirmation,
        LitanyEffectKind.Adoption,
        LitanyEffectKind.Ordination,
        LitanyEffectKind.Omission,
        LitanyEffectKind.Excommunication,
        LitanyEffectKind.InstallUpgrade,
        LitanyEffectKind.UninstallUpgrade,
        LitanyEffectKind.Reincarnation,
        LitanyEffectKind.Resurrection,
        LitanyEffectKind.MakeCruciform,
        LitanyEffectKind.RepairDoor,
        LitanyEffectKind.PowerBiogenerator,
        LitanyEffectKind.BioreactorSolution,
        LitanyEffectKind.BioreactorChamber,
        LitanyEffectKind.Scrying,
        LitanyEffectKind.DivineIntervention,
        LitanyEffectKind.HolyGuidance,
        LitanyEffectKind.OrderArmaments,
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
    public void PlannedFoundationEffectsAreNotRuntimeHandlers_ExceptImplemented()
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
        // BaptismalRecord stays foundation-gated: enabling it without a handler must fail closed.
        var errors = LitanyCatalogValidator.ValidateMissingHandler(
            "OxydLitanyBaptismalRecord",
            LitanyEffectKind.BaptismalRecord,
            isAvailable: true);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("without a registered runtime handler"));
    }

    [Test]
    public void PacketCImplementedEffectsAllowEnabledCatalogEntries()
    {
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.Relief), Is.True);
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.SoulHunger), Is.True);
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.Entreaty), Is.True);
        Assert.That(LitanyHandlerCatalog.AllowsEnabledCatalogEntry(LitanyEffectKind.CruciformSense), Is.True);
        Assert.That(
            LitanyCatalogValidator.ValidateMissingHandler(
                "OxydLitanyEntreaty",
                LitanyEffectKind.Entreaty,
                isAvailable: true),
            Is.Empty);
        Assert.That(
            LitanyCatalogValidator.ValidateMissingHandler(
                "OxydLitanyCruciformSense",
                LitanyEffectKind.CruciformSense,
                isAvailable: true),
            Is.Empty);
    }
}
