using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Cloning;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.12: the reader takes a cruciform through its real item slot, and the cloner's biomass gate
/// refuses an empty container but pays out of a stocked one.
/// </summary>
[TestOf(typeof(CruciformReaderSystem))]
public sealed class CruciformReaderTest : GameTest
{
    private static readonly EntProtoId CruciformReaderProto = "OxydNtCruciformReader";
    private static readonly EntProtoId CruciformClonerProto = "OxydNtCloner";
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly ItemSlotsSystem _slots = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _materialStorage = default!;
    [SidedDependency(Side.Server)] private readonly CruciformReaderSystem _reader = default!;

    [Test]
    public async Task ReaderReadsTheSoulOutOfTheInsertedCruciform()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var reader = SSpawnAtPosition(CruciformReaderProto, map.GridCoords);
            SComp<ApcPowerReceiverComponent>(reader).Powered = true;
            Assert.That(SComp<TransformComponent>(reader).Anchored, Is.True);
            var implant = SoulBearingCruciform(map.GridCoords);
            var name = SComp<CruciformSoulComponent>(implant).Name;

            Assert.That(_slots.TryInsert(reader, "cruciform", implant, null), Is.True,
                "Setup: the reader's slot must accept a cruciform.");
            Assert.That(SComp<CruciformReaderComponent>(reader).ReaderImplant, Is.EqualTo(implant),
                "Whatever is in the slot is the implant the reader reads.");

            Assert.That(_reader.TryReadSoul(reader, out var soul), Is.True);
            Assert.That(soul!.Name, Is.EqualTo(name), "The reader must hand back the cruciform's soul.");
        });
    }

    [Test]
    public async Task ClonerRefusesWhenEmptyAndSpendsWhenStocked()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cloner = SSpawnAtPosition(CruciformClonerProto, map.GridCoords);
            var pod = SComp<CloningPodComponent>(cloner);

            Assert.That(pod.RequiredMaterial.Id, Is.EqualTo("Biomatter"),
                "The cloner burns the port's biomatter, not upstream's biomass.");

            Assert.That(_reader.TrySpendBiomass(cloner, 50), Is.False, "An empty container must refuse to start.");
            Assert.That(_materialStorage.GetMaterialAmount(cloner, pod.RequiredMaterial), Is.EqualTo(0),
                "A refused start must spend nothing.");

            Assert.That(_materialStorage.TryChangeMaterialAmount(cloner, pod.RequiredMaterial, 100), Is.True,
                "Setup: the cloner must accept biomatter through its MaterialStorage.");

            Assert.That(_reader.TrySpendBiomass(cloner, 50), Is.True);
            Assert.That(_materialStorage.GetMaterialAmount(cloner, pod.RequiredMaterial), Is.EqualTo(50),
                "The cloner must consume biomatter.");
        });
    }

    /// <summary>
    /// Spawns an active bearer whose cruciform has had a soul snapshot written onto it.
    /// </summary>
    private EntityUid SoulBearingCruciform(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be active.");

        var comp = SComp<CruciformComponent>(implant!.Value);
        Assert.That(_modules.TryInstall(implant.Value, comp, CloningModule), Is.True,
            "Setup: the cloning module must install.");
        Assert.That(SComp<CruciformSoulComponent>(implant.Value).HasSnapshot, Is.True,
            "Setup: installing the cloning module must write a soul.");

        return implant.Value;
    }
}
