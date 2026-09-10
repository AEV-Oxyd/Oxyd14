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

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
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

    private EntityUid ImplantBody(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        return implant!.Value;
    }
}
