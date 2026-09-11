using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Cloning;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Implants;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.9: Reincarnation refreshes the cruciform's soul snapshot from the living wearer, and
/// Resurrection drives the reader/cloner rig into growing the dead wearer a new body that
/// carries their mind and their name.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsSoulTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId ReaderProto = "OxydNtCruciformReader";
    private static readonly EntProtoId ClonerProto = "OxydNtCloner";
    private static readonly ProtoId<LitanyPrototype> Reincarnation = "OxydLitanyReincarnation";
    private static readonly ProtoId<LitanyPrototype> Resurrection = "OxydLitanyResurrection";
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";

    private const string SoulSessionName = "soul_litany_test";
    private const string RebornName = "Reborn Test Subject";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CoreModuleSystem _modules = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly ItemSlotsSystem _slots = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _materialStorage = default!;
    [SidedDependency(Side.Server)] private readonly SharedMindSystem _minds = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly MetaDataSystem _meta = default!;

    [Test]
    public async Task Reincarnation_RefreshesTheSnapshotFromTheLivingWearer()
    {
        var map = await Pair.CreateTestMap();
        EntityUid implant = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            var target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)));
            implant = SComp<CruciformBearerComponent>(target).Cruciform!.Value;
            Assert.That(_modules.TryInstall(implant, SComp<CruciformComponent>(implant), CloningModule), Is.True,
                "Setup: the target's cruciform must carry the cloning module.");
            var stale = SComp<CruciformSoulComponent>(implant).Name;
            Assert.That(stale, Is.Not.Empty, "Setup: installing the cloning module must write a snapshot.");

            _meta.SetEntityName(target, RebornName);
            Assert.That(SComp<CruciformSoulComponent>(implant).Name, Is.EqualTo(stale),
                "Setup: renaming the body must not touch the stored snapshot.");

            var begin = _litany.TryBeginLitany(caster, Reincarnation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Reincarnation begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<CruciformSoulComponent>(implant).Name, Is.EqualTo(RebornName),
                "A completed Reincarnation must refresh the snapshot from the living wearer.");
        });
    }

    [Test]
    public async Task Resurrection_GrowsABodyCarryingTheStoredMindAndName()
    {
        var map = await Pair.CreateTestMap();
        var session = await Server.AddDummySession(SoulSessionName);

        EntityUid mindId = default;
        EntityUid cloner = default;
        string victimName = string.Empty;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            var reader = SSpawnAtPosition(ReaderProto, origin.Offset(new Vector2(1f, 0f)));
            cloner = SSpawnAtPosition(ClonerProto, origin.Offset(new Vector2(0f, 1f)));
            Assert.That(_materialStorage.TryChangeMaterialAmount(cloner, "Biomatter", 300), Is.True,
                "Setup: the cloner must accept biomatter.");

            var victim = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(0f, -1.5f)));
            var implant = _implants.AddImplant(victim, CruciformProto);
            Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
            Assert.That(_cruciform.Activate(victim), Is.True, "Setup: the victim must be active.");

            mindId = _minds.CreateMind(session.UserId, victimName = ServerName(victim)).Owner;
            _minds.TransferTo(mindId, victim);
            Assert.That(_minds.TryGetMind(victim, out var holder, out _) && holder == mindId, Is.True,
                "Setup: the victim must carry the mind.");

            Assert.That(_modules.TryInstall(implant!.Value, SComp<CruciformComponent>(implant.Value), CloningModule), Is.True,
                "Setup: the cloning module must install.");
            var soul = SComp<CruciformSoulComponent>(implant.Value);
            Assert.Multiple(() =>
            {
                Assert.That(soul.HasSnapshot, Is.True, "Setup: installing the module must write the soul.");
                Assert.That(soul.MindId, Is.EqualTo(mindId), "Setup: the snapshot must name the mind.");
            });

            victimName = ServerName(victim);
            _mobState.ChangeMobState(victim, MobState.Dead);
            Assert.That(_effects.TryExtractInstalledCruciform(victim, implant.Value), Is.True,
                "Setup: the soul-bearing cruciform must come out of the corpse.");
            Assert.That(_slots.TryInsert(reader, "cruciform", implant.Value, null), Is.True,
                "Setup: the reader's slot must accept the cruciform.");

            var begin = _litany.TryBeginLitany(caster, Resurrection, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Resurrection begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<MindComponent>(mindId, out var mind), Is.True);
            Assert.That(mind!.OwnedEntity, Is.Not.Null, "A completed Resurrection must move the stored mind.");
            var clone = mind.OwnedEntity!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.TryGetComponent<MindContainerComponent>(clone, out var holder) && holder.Mind == mindId, Is.True,
                    "The new body must carry the mind.");
                Assert.That(ServerName(clone), Is.EqualTo(victimName),
                    "The new body must carry the dead wearer's name.");
            });

            Assert.That(SComp<CloningPodComponent>(cloner).BodyContainer.ContainedEntity, Is.EqualTo(clone),
                "The new body must be the cloner's own job, not a body grown elsewhere.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>Server-side display name of an entity.</summary>
    private string ServerName(EntityUid uid)
        => SEntMan.GetComponent<MetaDataComponent>(uid).EntityName;

    /// <summary>A human with an active cruciform.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be active.");
        return body;
    }

    /// <summary>Waits out the cast DoAfter / extra delay until no pending cast remains.</summary>
    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 60; i++)
        {
            await Pair.RunTicksSync(5);
            var done = false;
            await Server.WaitPost(() => done = _litany.TestingPendingCount == 0);
            if (done)
                return;
        }

        Assert.Fail("Cast did not complete within expected ticks.");
    }
}
