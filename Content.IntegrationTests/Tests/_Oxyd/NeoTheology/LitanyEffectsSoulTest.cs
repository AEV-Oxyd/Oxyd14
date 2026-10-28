using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared.Humanoid;
using Content.Shared.Forensics.Components;
using Content.Shared.Preferences;
using Content.Shared.Body;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Cloning;
using Content.Shared.Damage.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Implants;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Reincarnation restores a saved soul; Resurrection grows an unoccupied matching vessel.
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

    public override PoolSettings PoolSettings => new() { InLobby = true };

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
    public async Task Reincarnation_RestoresSavedMindAndIdentity_NotThePreparedBodySnapshot()
    {
        var map = await Pair.CreateTestMap();
        EntityUid implant = default;
        EntityUid target = default;
        EntityUid mindId = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            var original = SpawnBearer(origin.Offset(new Vector2(10f, 0f)));
            _meta.SetEntityName(original, RebornName);
            mindId = _minds.CreateMind(null, RebornName).Owner;
            _minds.TransferTo(mindId, original);
            implant = SComp<CruciformBearerComponent>(original).Cruciform!.Value;
            _mobState.ChangeMobState(original, MobState.Dead);
            Assert.That(_effects.TryExtractInstalledCruciform(original, implant), Is.True);

            target = SSpawnAtPosition(HumanProto, origin.Offset(Vector2.UnitX));
            var dna = SComp<DnaComponent>(target);
            dna.DNA = SComp<CruciformSoulComponent>(implant).Dna;
            _meta.SetEntityName(target, "Unoccupied vessel, not the saved identity");
            Assert.That(_effects.TryImplantLooseCruciform(target, implant), Is.True);
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.False);
            Assert.That(_cruciform.Activate(target), Is.False, "Baptism must not replace a saved soul.");
            var begin = _litany.TryBeginLitany(caster, Reincarnation, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id);
        });

        await AdvancePastCast();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<MindContainerComponent>(target).Mind, Is.EqualTo(mindId));
            Assert.That(SComp<MindComponent>(mindId).OwnedEntity, Is.EqualTo(target));
            Assert.That(ServerName(target), Is.EqualTo(RebornName));
            Assert.That(SComp<CruciformSoulComponent>(implant).MindId, Is.EqualTo(mindId));
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.True);
        });
    }

    [TestCase("dna")]
    [TestCase("occupied")]
    [TestCase("original-living")]
    [TestCase("godblood")]
    public async Task Reincarnation_InvalidVesselCannotReplaceTheSavedSoul(string failure)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var original = SpawnBearer(TileCentre(map.GridCoords));
            var mindId = _minds.CreateMind(null, RebornName).Owner;
            _minds.TransferTo(mindId, original);
            var implant = SComp<CruciformBearerComponent>(original).Cruciform!.Value;
            _mobState.ChangeMobState(original, MobState.Dead);
            Assert.That(_effects.TryExtractInstalledCruciform(original, implant), Is.True);
            var soul = SComp<CruciformSoulComponent>(implant);
            var savedName = soul.Name;
            var target = SSpawnAtPosition(HumanProto, TileCentre(map.GridCoords).Offset(Vector2.UnitX));
            SComp<DnaComponent>(target).DNA = failure == "dna" ? "wrong-vessel" : soul.Dna;
            Assert.That(_effects.TryImplantLooseCruciform(target, implant), Is.True);
            if (failure == "occupied")
                _minds.TransferTo(_minds.CreateMind(null, "Other soul").Owner, target);
            else if (failure == "original-living")
                _mobState.ChangeMobState(original, MobState.Alive);
            else if (failure == "godblood")
                SEntMan.EnsureComponent<GodbloodMutationComponent>(target);

            var reincarnate = new LitanyReincarnationEvent(target, false);
            SEntMan.EventBus.RaiseLocalEvent(target, ref reincarnate);
            Assert.That(reincarnate.Handled, Is.False);
            Assert.That(soul.MindId, Is.EqualTo(mindId));
            Assert.That(soul.Name, Is.EqualTo(savedName));
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.False);
        });
    }

    [TestCase(false, "OxydNtDisciple", 60)]
    [TestCase(true, "OxydNtDisciple", 60)]
    [TestCase(false, "OxydNtAgrolyte", 60)]
    [TestCase(false, "OxydNtCustodian", 60)]
    [TestCase(false, "OxydNtAcolyte", 0)]
    [TestCase(false, "OxydNtPreacher", 0)]
    [TestCase(false, "OxydNtInquisitor", 0)]
    [TestCase(true, "OxydNtDisciple", 60, true)]
    public async Task Resurrection_GrowsTheSavedBodyDespiteCorpseChangesOrDeletion(
        bool deleteCorpse, string casterProfile, int cellularDamage, bool biologicalRejection = false)
    {
        var map = await Pair.CreateMachineTestMap();
        var session = await Server.AddDummySession(SoulSessionName);

        EntityUid mindId = default;
        EntityUid cloner = default;
        EntityUid clone = default;
        string victimName = string.Empty;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            if (casterProfile != "OxydNtDisciple")
                Assert.That(_cruciform.TrySetProfile(caster, casterProfile), Is.True);
            var reader = SSpawnAtPosition(ReaderProto, origin.Offset(new Vector2(0f, 1f)));
            cloner = SSpawnAtPosition(ClonerProto, origin.Offset(new Vector2(1f, 0f)));
            SComp<CruciformClonerComponent>(cloner).Reader = reader;
            foreach (var machine in new[] { reader, cloner })
            {
                SComp<ApcPowerReceiverComponent>(machine).NeedsPower = false;
                SComp<ApcPowerReceiverComponent>(machine).Powered = true;
                Assert.That(SComp<TransformComponent>(machine).Anchored, Is.True);
            }
            Assert.That(_materialStorage.TryChangeMaterialAmount(cloner, "Biomatter", 300), Is.True,
                "Setup: the cloner must accept biomatter.");

            var victim = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(0f, -1.5f)));
            var savedProfile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAge(55);
            savedProfile.Appearance.EyeColor = Color.Blue;
            SEntMan.System<HumanoidProfileSystem>().ApplyProfileTo(victim, savedProfile);
            SEntMan.System<SharedVisualBodySystem>().ApplyProfileTo(victim, savedProfile);
            _meta.SetEntityName(victim, RebornName);
            if (biologicalRejection)
                SEntMan.AddComponent<AtheistMutationComponent>(victim);
            var implant = _implants.AddImplant(victim, CruciformProto);
            Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
            Assert.That(_cruciform.Activate(victim), Is.True, "Setup: the victim must be active.");

            mindId = _minds.CreateMind(session.UserId, victimName = ServerName(victim)).Owner;
            _minds.TransferTo(mindId, victim);
            Assert.That(_minds.TryGetMind(victim, out var holder, out _) && holder == mindId, Is.True,
                "Setup: the victim must carry the mind.");

            Assert.That(SComp<CruciformComponent>(implant!.Value).InstalledModules, Does.Contain(CloningModule),
                "Normal activation supplies cloning; mind assignment refreshes its snapshot.");
            var soul = SComp<CruciformSoulComponent>(implant.Value);
            Assert.Multiple(() =>
            {
                Assert.That(soul.HasSnapshot, Is.True, "Setup: installing the module must write the soul.");
                Assert.That(soul.MindId, Is.EqualTo(mindId), "Setup: the snapshot must name the mind.");
            });

            victimName = ServerName(victim);
            Assert.That(soul.AtheistMutation, Is.EqualTo(biologicalRejection));
            Assert.That(soul.Profile!.Age, Is.EqualTo(55));
            Assert.That(soul.Profile.Appearance.EyeColor, Is.EqualTo(Color.Blue));
            _mobState.ChangeMobState(victim, MobState.Dead);
            SEntMan.RemoveComponent<AtheistMutationComponent>(victim);
            _meta.SetEntityName(victim, "Changed after snapshot");
            var changedProfile = savedProfile.WithAge(80);
            changedProfile.Appearance.EyeColor = Color.Red;
            SEntMan.System<HumanoidProfileSystem>().ApplyProfileTo(victim, changedProfile);
            SEntMan.System<SharedVisualBodySystem>().ApplyProfileTo(victim, changedProfile);
            Assert.That(_effects.TryExtractInstalledCruciform(victim, implant.Value), Is.True,
                "Setup: the soul-bearing cruciform must come out of the corpse.");
            Assert.That(_slots.TryInsert(reader, "cruciform", implant.Value, null), Is.True,
                "Setup: the reader's slot must accept the cruciform.");

            if (deleteCorpse)
                SEntMan.DeleteEntity(victim);
            var readers = SEntMan.System<CruciformReaderSystem>();
            Assert.That(readers.CanResurrect(cloner, reader), Is.True);
            SComp<ApcPowerReceiverComponent>(reader).Powered = false;
            Assert.That(readers.CanResurrect(cloner, reader), Is.False);
            SComp<ApcPowerReceiverComponent>(reader).Powered = true;
            Assert.That(_materialStorage.TryChangeMaterialAmount(cloner, "Biomatter", -300), Is.True);
            Assert.That(readers.CanResurrect(cloner, reader), Is.False);
            Assert.That(_materialStorage.TryChangeMaterialAmount(cloner, "Biomatter", 300), Is.True);
            var validation = new LitanyResurrectionEvent(cloner, reader, false, true);
            SEntMan.EventBus.RaiseLocalEvent(cloner, ref validation);
            Assert.That(validation.Handled, Is.True);
            Assert.That(_materialStorage.GetMaterialAmount(cloner, "Biomatter"), Is.EqualTo(300));
            Assert.That(SComp<CloningPodComponent>(cloner).BodyContainer.ContainedEntity, Is.Null);
            var begin = _litany.TryBeginLitany(caster, Resurrection, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Resurrection begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<MindComponent>(mindId, out var mind), Is.True);
            Assert.That(SComp<CloningPodComponent>(cloner).BodyContainer.ContainedEntity, Is.Not.Null);
            clone = SComp<CloningPodComponent>(cloner).BodyContainer.ContainedEntity!.Value;
            Assert.That(mind!.OwnedEntity, Is.Not.EqualTo(clone), "Resurrection grows an unoccupied vessel.");

            Assert.Multiple(() =>
            {
                Assert.That(SComp<MindContainerComponent>(clone).Mind, Is.Null,
                    "Only Reincarnation, not body growth, restores the saved mind.");
                Assert.That(ServerName(clone), Is.EqualTo(victimName),
                    "The new body must carry the saved name.");
                Assert.That(SComp<HumanoidProfileComponent>(clone).Age, Is.EqualTo(55),
                    "The new body must use the saved profile, not the corpse profile.");
            });

            var eyesFound = false;
            var organs = SEntMan.EntityQueryEnumerator<OrganComponent, VisualOrganComponent>();
            while (organs.MoveNext(out _, out var organ, out var visual))
            {
                var layer = visual.Layer;
                if (organ.Body != clone || !layer.Equals(HumanoidVisualLayers.Eyes))
                    continue;
                eyesFound = true;
                Assert.That(visual.Profile.EyeColor, Is.EqualTo(Color.Blue));
            }
            Assert.That(eyesFound, Is.True);
            Assert.That(SEntMan.HasComponent<AtheistMutationComponent>(clone), Is.EqualTo(biologicalRejection));

            SEntMan.System<DamageableSystem>().GetAllDamage(clone).DamageDict.TryGetValue("Cellular", out var injury);
            Assert.That(injury.Float(), Is.EqualTo(cellularDamage), "The caster's rank must determine the resurrection injury.");
            var pod = SComp<CloningPodComponent>(cloner);
            Assert.That(pod.BodyContainer.ContainedEntity, Is.EqualTo(clone),
                "The new body must grow in the selected pod.");
            pod.CloningProgress = pod.CloningTime;
        });

        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<CloningPodComponent>(cloner).BodyContainer.ContainedEntity, Is.Null,
                "The pod must release the body after growth.");
            Assert.That(ServerName(clone), Is.EqualTo(victimName));
            Assert.That(SComp<MindContainerComponent>(clone).Mind, Is.Null);
            SEntMan.System<DamageableSystem>().GetAllDamage(clone).DamageDict.TryGetValue("Cellular", out var injury);
            Assert.That(injury.Float(), Is.EqualTo(cellularDamage), "Normal pod ejection must not erase the injury.");
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
