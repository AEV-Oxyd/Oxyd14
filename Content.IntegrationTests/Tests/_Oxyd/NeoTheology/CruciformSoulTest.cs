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
/// P2.11: the cloning module writes the wearer's soul onto the cruciform, and the soul survives
/// the module being pulled — that is the resurrection path.
/// </summary>
[TestOf(typeof(CoreModuleBehaviorSystem))]
public sealed class CruciformSoulTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task InstallingCloningModuleSnapshotsTheWearer()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (body, implant) = Wearer(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);
            var bodyName = SEntMan.GetComponent<MetaDataComponent>(body).EntityName;

            Assert.That(_modules.TryInstall(implant, comp, CloningModule), Is.True);

            var soul = SComp<CruciformSoulComponent>(implant);
            Assert.Multiple(() =>
            {
                Assert.That(soul.HasSnapshot, Is.True,
                    "Installing the cloning module must write the soul.");
                Assert.That(soul.Name, Is.EqualTo(bodyName),
                    "The snapshot must carry the wearer's name.");
            });
        });
    }

    [Test]
    public async Task SnapshotSurvivesModuleRemoval()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (_, implant) = Wearer(map.GridCoords);
            var comp = SComp<CruciformComponent>(implant);
            Assert.That(_modules.TryInstall(implant, comp, CloningModule), Is.True);

            var before = SComp<CruciformSoulComponent>(implant).Name;
            Assert.That(before, Is.Not.Empty, "Setup: the module must have written a snapshot.");

            Assert.That(_modules.TryRemove(implant, comp, CloningModule), Is.True);

            var soul = SComp<CruciformSoulComponent>(implant);
            Assert.Multiple(() =>
            {
                Assert.That(soul.HasSnapshot, Is.True, "Removing the module must not lose the soul.");
                Assert.That(soul.Name, Is.EqualTo(before), "The snapshot must survive uninstall.");
            });
        });
    }

    /// <summary>Spawns a body carrying an active cruciform.</summary>
    private (EntityUid Body, EntityUid Implant) Wearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be active.");
        return (body, implant!.Value);
    }
}
