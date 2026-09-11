using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.FixedPoint;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

public sealed class LitanyPrototypeTest : GameTest
{
    [Test]
    public async Task LoadedCatalogHasNoStructuralValidationErrors()
    {
        var server = Pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var localization = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var errors = LitanyCatalogValidator.Validate(prototypes, localization);
            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
            Assert.That(server.System<LitanyPrototypeValidationSystem>().CatalogReady, Is.True);
        });
    }

    [Test]
    public async Task CatalogHasExpectedAvailabilityAndStableIdentity()
    {
        var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
        var litanies = prototypes.EnumeratePrototypes<LitanyPrototype>().ToArray();

        Assert.That(litanies, Has.Length.EqualTo(LitanyCatalogValidator.ExpectedLitanyCount));
        Assert.That(litanies.Count(litany => litany.IsAvailable), Is.EqualTo(49));
        Assert.That(litanies.Where(l => l.IsAvailable).Select(l => l.Effect),
            Is.EquivalentTo(new[]
            {
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
                LitanyEffectKind.Initiation,
                LitanyEffectKind.Sending,
                LitanyEffectKind.BaptismalRecord,
                LitanyEffectKind.AcceleratedGrowth,
                LitanyEffectKind.Rejection,
                LitanyEffectKind.RevealAdversaries,
                LitanyEffectKind.WordsOfPurging,
                LitanyEffectKind.Atonement,
                LitanyEffectKind.Penance,
                LitanyEffectKind.Asacris,
                LitanyEffectKind.DivineGuidance,
                LitanyEffectKind.Manifestation,
                LitanyEffectKind.Uproot,
                LitanyEffectKind.Knowledge,
                LitanyEffectKind.Bounty,
            }));
        Assert.That(litanies.Count(litany => litany.Dependency == NeoTheologyDependency.None),
            Is.EqualTo(LitanyCatalogValidator.ExpectedFoundationLitanyCount));
        Assert.That(litanies.Count(litany => litany.Dependency != NeoTheologyDependency.None),
            Is.EqualTo(LitanyCatalogValidator.ExpectedDependencyGatedLitanyCount));
        Assert.That(litanies.Select(litany => litany.ID), Is.Unique);
        Assert.That(litanies.Select(litany => litany.Effect), Is.Unique);

        foreach (var litany in litanies)
        {
            Assert.That(litany.ID, Is.EqualTo($"OxydLitany{litany.Effect}"));
        }
    }

    [Test]
    public async Task Effects_ResolveToTypedEffectClasses()
    {
        var prototypes = Pair.Server.ResolveDependency<IPrototypeManager>();
        var litanies = prototypes.EnumeratePrototypes<LitanyPrototype>().ToDictionary(l => l.ID);

        var relief = litanies["OxydLitanyRelief"];
        Assert.That(relief.Effects, Has.Count.EqualTo(1), "Relief must declare exactly one effect.");
        Assert.That(relief.Effects[0], Is.InstanceOf<LitanyHealEffect>());
        Assert.That(((LitanyHealEffect) relief.Effects[0]).Damage.DamageDict["Blunt"],
            Is.EqualTo(FixedPoint2.New(-5)), "Relief Blunt heal value drifted.");

        var soulHunger = litanies["OxydLitanySoulHunger"];
        Assert.That(soulHunger.Effects, Has.Count.EqualTo(1));
        Assert.That(soulHunger.Effects[0], Is.InstanceOf<LitanySoulHungerEffect>());
        Assert.That(((LitanySoulHungerEffect) soulHunger.Effects[0]).Damage.DamageDict["Heat"],
            Is.EqualTo(FixedPoint2.New(5)), "SoulHunger Heat injury must be stored positive.");

        Assert.That(litanies["OxydLitanyEntreaty"].Effects[0], Is.InstanceOf<LitanyEntreatyEffect>());
        Assert.That(litanies["OxydLitanyCruciformSense"].Effects[0], Is.InstanceOf<LitanyCruciformSenseEffect>());
        Assert.That(litanies["OxydLitanyCommitment"].Effects[0], Is.InstanceOf<LitanyCommitmentEffect>());
        Assert.That(litanies["OxydLitanyDeprivation"].Effects[0], Is.InstanceOf<LitanyDeprivationEffect>());

        var grace = litanies["OxydLitanyGraceOfPerseverance"];
        Assert.That(grace.Effects, Has.Count.EqualTo(1));
        Assert.That(grace.Effects[0], Is.InstanceOf<LitanySkillEffect>());
        Assert.That(((LitanySkillEffect) grace.Effects[0]).Amounts["Mec"], Is.EqualTo(10));

        foreach (var litany in litanies.Values.Where(l => l.IsAvailable))
        {
            Assert.That(litany.Effects, Is.Not.Empty,
                $"{litany.ID} is available but declares no effects.");
        }
    }

    [Test]
    public async Task CatalogSerializationRoundTrips()
    {
        var server = Pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var serialization = server.ResolveDependency<ISerializationManager>();
        var context = new PrototypeSaveTest.TestEntityUidContext(serialization);

        await server.WaitAssertion(() =>
        {
            foreach (var kind in new[]
                     {
                         typeof(LitanyPrototype),
                         typeof(LitanySetPrototype),
                         typeof(NeoTheologyRulesPrototype),
                         typeof(NeoTheologyProfilePrototype),
                         })
            {
                foreach (var prototype in prototypes.EnumeratePrototypes(kind))
                {
                    var written = serialization.WriteValue(kind, prototype, alwaysWrite: true, context: context);
                    var reloaded = serialization.Read(kind, written, context: context);
                    Assert.That(reloaded, Is.Not.Null, $"Failed to round-trip {kind.Name} {prototype.ID}.");
                    Assert.That(((IPrototype) reloaded!).ID, Is.EqualTo(prototype.ID));
                    var rewritten = serialization.WriteValue(kind, reloaded, alwaysWrite: true, context: context);
                    Assert.That(rewritten.ToString(), Is.EqualTo(written.ToString()),
                        $"Round-trip changed serialized fields for {kind.Name} {prototype.ID}.");
                }
            }
        });
    }
}
