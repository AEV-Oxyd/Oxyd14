using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
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
        Assert.That(litanies.Count(litany => litany.IsAvailable), Is.EqualTo(2));
        Assert.That(litanies.Where(l => l.IsAvailable).Select(l => l.Effect),
            Is.EquivalentTo(new[] { LitanyEffectKind.Relief, LitanyEffectKind.SoulHunger }));
        Assert.That(litanies.Count(litany => litany.Dependency == NeoTheologyDependency.None),
            Is.EqualTo(LitanyCatalogValidator.ExpectedFoundationLitanyCount));
        Assert.That(litanies.Count(litany => litany.Dependency != NeoTheologyDependency.None),
            Is.EqualTo(LitanyCatalogValidator.ExpectedDependencyGatedLitanyCount));
        Assert.That(litanies.Select(litany => litany.ID), Is.Unique);
        Assert.That(litanies.Select(litany => litany.Effect), Is.Unique);

        foreach (var litany in litanies)
        {
            Assert.That(litany.ID, Is.EqualTo($"OxydLitany{litany.Effect}"));
            Assert.That(litany.SourcePath, Is.Not.Empty, litany.ID);
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
                         typeof(NeoTheologySpecializationPrototype),
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
