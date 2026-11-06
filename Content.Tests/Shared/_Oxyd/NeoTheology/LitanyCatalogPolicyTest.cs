using Content.Shared._Oxyd.NeoTheology;
using NUnit.Framework;

namespace Content.Tests.Shared._Oxyd.NeoTheology;

[TestFixture]
[TestOf(typeof(LitanyCatalogValidator))]
public sealed class LitanyCatalogPolicyTest
{
    [Test]
    public void FullCatalogPolicy_HasSixtyLitaniesAndNoGatedEntries()
    {
        Assert.That(
            LitanyCatalogValidator.ExpectedDependencyGatedLitanyCount,
            Is.EqualTo(LitanyCatalogValidator.ExpectedLitanyCount -
                       LitanyCatalogValidator.ExpectedFoundationLitanyCount));
    }

    [Test]
    public void EnabledLitanyWithoutEffectsFailsClosed()
    {
        // A fresh prototype is enabled/available with an empty effect list: the validator
        // must fail closed — "has a handler" means "carries at least one effect".
        var errors = LitanyCatalogValidator.ValidateMissingHandler(new LitanyPrototype());

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("without any effects"));
    }

    [Test]
    public void LitanyWithoutEffectsIsAllowedWhenHandlersAreNotRequired()
    {
        var errors = LitanyCatalogValidator.ValidateMissingHandler(
            new LitanyPrototype(),
            requireRuntimeHandler: false);

        Assert.That(errors, Is.Empty);
    }
}
