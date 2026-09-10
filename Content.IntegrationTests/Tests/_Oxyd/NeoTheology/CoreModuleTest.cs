using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Phase 1 cruciform module host: install/uninstall bookkeeping and the derived
/// litany-set/capacity state recomputed by <see cref="CruciformSystem"/>.
/// </summary>
[TestOf(typeof(CoreModuleSystem))]
public sealed class CoreModuleTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<CoreModulePrototype> PriestModule = "OxydNtModulePriest";
    private static readonly ProtoId<CoreModulePrototype> CustodianModule = "OxydNtModuleCustodian";
    private static readonly ProtoId<CoreModulePrototype> RedLightModule = "OxydNtModuleRedLight";
    private static readonly ProtoId<CoreModulePrototype> InquisitorModule = "OxydNtModuleInquisitor";
    private static readonly ProtoId<LitanySetPrototype> CustodianSet = "OxydLitanyCustodian";
    private static readonly ProtoId<LitanySetPrototype> CommonSet = "OxydLitanyCommon";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task InstallAndUninstallModuleUpdatesInstalledSet()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var implant = ImplantBody(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);

            Assert.That(_modules.TryInstall(implant, comp, PriestModule), Is.True);
            Assert.That(_modules.HasModule(comp, PriestModule), Is.True);
            Assert.That(comp.InstalledModules, Does.Contain(PriestModule));

            Assert.That(_modules.TryInstall(implant, comp, PriestModule), Is.False,
                "Re-installing an already-installed module must be a no-op.");
            Assert.That(_modules.TryInstall(implant, comp, "OxydNtModuleDoesNotExist"), Is.False,
                "Unknown module ids must be rejected.");

            Assert.That(_modules.TryRemove(implant, comp, PriestModule), Is.True);
            Assert.That(_modules.HasModule(comp, PriestModule), Is.False);
            Assert.That(_modules.TryRemove(implant, comp, PriestModule), Is.False,
                "Removing a module that is not installed must be a no-op.");
        });
    }

    [Test]
    public async Task InstallingCustodianModuleUnlocksCustodianSet()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var implant = ImplantBody(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);

            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtDisciple"));
            Assert.That(comp.UnlockedSets, Does.Contain(CommonSet));
            Assert.That(comp.UnlockedSets, Does.Not.Contain(CustodianSet),
                "The disciple profile must not grant the custodian set.");

            Assert.That(_modules.TryInstall(implant, comp, CustodianModule), Is.True);

            Assert.That(comp.UnlockedSets, Does.Contain(CustodianSet),
                "Installing the custodian module must unlock its litany set, even while inactive.");
            Assert.That(comp.UnlockedSets, Does.Contain(CommonSet),
                "Profile sets must survive the module union.");
        });
    }

    [Test]
    public async Task RedLightModuleRaisesCapacityBySixtyPercent()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var implant = ImplantBody(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);

            var before = comp.MaxHoliness;
            Assert.That(before, Is.GreaterThan(0d));

            Assert.That(_modules.TryInstall(implant, comp, RedLightModule), Is.True);

            var after = comp.MaxHoliness;
            Assert.That(after / before, Is.EqualTo(1.6).Within(0.001),
                "Red light multiplies cruciform capacity by 1.6.");

            Assert.That(_modules.TryRemove(implant, comp, RedLightModule), Is.True);
            Assert.That(comp.MaxHoliness, Is.EqualTo(before).Within(0.001),
                "Removing the module must restore the base capacity.");
        });
    }

    [Test]
    public async Task OrdinationReplacesPriestModulesWithInquisitor()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var implant = ImplantBody(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);

            _cruciform.MakePriest(implant, comp);
            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtPreacher"));
            Assert.That(comp.InstalledModules, Does.Not.Contain(InquisitorModule),
                "A preacher must not carry the inquisitor module.");

            var priestCount = comp.InstalledModules.Count;

            _cruciform.MakeInquisitor(implant, comp);

            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtInquisitor"));
            Assert.That(comp.InstalledModules, Does.Contain(InquisitorModule));
            Assert.That(comp.InstalledModules, Does.Contain(RedLightModule));
            Assert.That(comp.InstalledModules.Count, Is.EqualTo(priestCount + 2),
                "Ordination swaps the rank modules in, it does not stack profiles.");
            // Inquisitor profile capacity (100) x inquisitor module (2.0) x red light (1.6).
            Assert.That(comp.MaxHoliness, Is.EqualTo(100d * 2.0 * 1.6).Within(0.001));
        });
    }

    private EntityUid ImplantBody(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        return implant!.Value;
    }
}
