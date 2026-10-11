using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Power.Components;
using Content.Shared._Oxyd;
using Content.Shared._Oxyd.Medical;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DetailExaminable;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Warps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

[TestOf(typeof(CruciformSystem))]
public sealed class NeoTheologyFeatureGapTest : SocialNoticeGameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, DummyTicker = false };
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CoreModuleBehaviorSystem _souls = default!;
    [SidedDependency(Side.Server)] private readonly SharedSkillSystem _skills = default!;
    [SidedDependency(Side.Server)] private readonly SharedMindSystem _minds = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mob = default!;
    [SidedDependency(Side.Server)] private readonly NeoTheologyFoundationSystem _foundation = default!;
    [SidedDependency(Side.Server)] private readonly EyeOfTheProtectorSystem _eye = default!;
    [SidedDependency(Side.Server)] private readonly ObeliskSystem _obelisk = default!;
    [SidedDependency(Side.Server)] private readonly BioreactorSystem _reactor = default!;
    [SidedDependency(Side.Server)] private readonly ArmamentsPrinterSystem _printer = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly SharedRoleSystem _roles = default!;
    [SidedDependency(Side.Server)] private readonly NtUplinkSystem _uplink = default!;
    [SidedDependency(Side.Server)] private readonly NeoTheologyWorldSystem _world = default!;
    [SidedDependency(Side.Server)] private readonly NeoTheologyArtifactSystem _artifacts = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damage = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;

    [TestCase("RitualBook", 1)]
    [TestCase("Cruciform", 1)]
    [TestCase("PreacherHat", 3)]
    [TestCase("PreacherCoat", 3)]
    [TestCase("AscensionKit", 3)]
    [TestCase("RitualBlade", 2)]
    [TestCase("Lightfall", 10)]
    [TestCase("Halicon", 8)]
    [TestCase("Dominion", 10)]
    [TestCase("Purger", 10)]
    [TestCase("Nemesis", 5)]
    [TestCase("Themis", 8)]
    [TestCase("Valkyrie", 13)]
    public async Task SourceUplinkProductSpawnsAtItsSourcePriceWithActiveBearerAuthorization(string product, int cost)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var listing = SProtoMan.Index<ListingPrototype>("OxydNtUplink" + product);
            Assert.That(listing.OriginalCost["Telecrystal"].Float(), Is.EqualTo((float) cost));
            Assert.That(listing.Conditions, Has.Some.InstanceOf<NeoTheologyUplinkCondition>());
            Assert.That(listing.ProductEntity, Is.Not.Null);
            var entity = SSpawnAtPosition(listing.ProductEntity!.Value, map.GridCoords);
            Assert.That(SEntMan.Deleted(entity), Is.False);
        });
    }

    [Test]
    public async Task SavedBodyRestoresSkillsLanguagesBloodFlavorAndHolyLightWithoutAliasing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords);
            var implant = Implant(body);
            var skills = SEntMan.EnsureComponent<MobSkillComponent>(body);
            skills.skills["Cog"] = [37, 0];
            _skills.AddBuff((body, skills), "SavedBlessing", 5, "Cog", TimeSpan.FromMinutes(10));
            var deadline = skills.buffSources["Cog"]["SavedBlessing"].Single().expires;
            var languages = SEntMan.EnsureComponent<LanguageKnowledgeComponent>(body);
            languages.chosen = "Universal";
            languages.speaking.Add("Universal");
            languages.understanding.Add("Universal");
            SEntMan.EnsureComponent<DetailExaminableComponent>(body).Content = "Remember this soul.";
            SEntMan.EnsureComponent<HolyLightComponent>(body);
            Assert.That(_souls.WriteSnapshot(implant, SComp<CruciformComponent>(implant)), Is.True);
            var soul = SComp<CruciformSoulComponent>(implant);
            var clone = SSpawnAtPosition("MobHuman", map.GridCoords);
            _souls.RestoreBodyState(clone, soul);
            var restored = SComp<MobSkillComponent>(clone);
            Assert.That(restored.skills["Cog"][0], Is.EqualTo(37));
            Assert.That(restored.buffSources["Cog"]["SavedBlessing"].Single().expires, Is.EqualTo(deadline));
            Assert.That(SComp<LanguageKnowledgeComponent>(clone).speaking, Does.Contain(languages.chosen));
            Assert.That(SComp<DetailExaminableComponent>(clone).Content, Is.EqualTo("Remember this soul."));
            Assert.That(SEntMan.HasComponent<HolyLightComponent>(clone), Is.True);
            Assert.That(SComp<BloodstreamComponent>(clone).BloodReferenceSolution, Is.Not.SameAs(soul.BloodReference));
            restored.skills["Cog"][0] = 1;
            SComp<LanguageKnowledgeComponent>(clone).speaking.Clear();
            Assert.That(soul.BaseSkills["Cog"], Is.EqualTo(37));
            Assert.That(soul.Speaking, Does.Contain(languages.chosen));
        });
    }

    [Test]
    public async Task PeriodicPurityRejectsForeignImplantsButPreservesResistanceAndGodblood()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords);
            var rejected = _implants.AddImplant(body, "FreedomImplant")!.Value;
            var resistant = _implants.AddImplant(body, "TrackingImplant")!.Value;
            SEntMan.EnsureComponent<CruciformResistantComponent>(resistant);
            SComp<CruciformComponent>(Implant(body)).NextPurity = TimeSpan.Zero;
            _cruciform.Update(0);
            Assert.That(SEntMan.HasComponent<RejectedImplantComponent>(rejected), Is.True);
            Assert.That(SComp<ImplantedComponent>(body).ImplantContainer.Contains(rejected), Is.False);
            Assert.That(SComp<ImplantedComponent>(body).ImplantContainer.Contains(resistant), Is.True);
            _implants.ForceImplant(body, (rejected, SComp<SubdermalImplantComponent>(rejected)));
            Assert.That(SComp<ImplantedComponent>(body).ImplantContainer.Contains(rejected), Is.False);
            Assert.That(SComp<SubdermalImplantComponent>(rejected).ImplantedEntity, Is.Null);
            var godblood = Bearer(map.GridCoords);
            SEntMan.EnsureComponent<GodbloodMutationComponent>(godblood);
            var spared = _implants.AddImplant(godblood, "FreedomImplant")!.Value;
            Assert.That(_foundation.Purify(godblood, cleanseMutation: true), Is.Zero);
            Assert.That(SComp<ImplantedComponent>(godblood).ImplantContainer.Contains(spared), Is.True);
        });
    }

    [Test]
    public async Task HardEjectionUnlinksTheSoulAndInflictsSourceInjuries()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords);
            var implant = Implant(body);
            Assert.That(_foundation.TryHardEject(body), Is.True);
            Assert.That(SComp<CruciformComponent>(implant).ImplantedEntity, Is.Null);
            Assert.That(SComp<CruciformComponent>(implant).Active, Is.False);
            var damage = _damage.GetAllDamage((body, SComp<DamageableComponent>(body))).DamageDict;
            Assert.That(damage["Cellular"].Float(), Is.InRange(55f, 60f));
            Assert.That(damage["Asphyxiation"].Float(), Is.InRange(100f, 150f));
            Assert.That(damage["Heat"].Float(), Is.InRange(100f, 175f));
            Assert.That(damage["Radiation"].Float(), Is.InRange(40f, 60f));
        });
    }

    [Test]
    public async Task ActivationObservationAndEnergyMiracleReachFaithfulOnAnotherMap()
    {
        var first = await Pair.CreateTestMap();
        var second = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = Powered("OxydNtEyeOfTheProtector", first.GridCoords);
            var state = SComp<EyeOfTheProtectorComponent>(eye);
            var body = Bearer(first.GridCoords);
            Assert.That(state.Observation, Is.EqualTo(50));
            var remote = Bearer(second.GridCoords);
            var implant = Implant(remote);
            _cruciform.RecomputeProfile(implant, SComp<CruciformComponent>(implant));
            var before = SComp<CruciformComponent>(implant).RegenerationPerSecond;
            _eye.FireMiracle(eye, NeoTheologyMiracle.Energy);
            Assert.That(SComp<CruciformComponent>(implant).EnergyMiracles, Is.EqualTo(1));
            Assert.That(SComp<CruciformComponent>(implant).RegenerationPerSecond, Is.GreaterThan(before));
            Assert.That(_eye.EnumerateFaithful().Select(f => f.Body), Does.Contain(remote));
            _foundation.TryHardEject(body);
            Assert.That(state.Observation, Is.Zero);
        });
    }

    [Test]
    public async Task AlertReportsAnExplicitSourceThreatToThePreacherOnly()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var preacher = Bearer(map.GridCoords, "OxydNtPreacher");
            var disciple = Bearer(map.GridCoords);
            var enemy = SSpawnAtPosition("MobHuman", map.GridCoords.Offset(new Vector2(3, 0)));
            SEntMan.EnsureComponent<NeoTheologyThreatComponent>(enemy);
            var eye = Powered("OxydNtEyeOfTheProtector", map.GridCoords);
            _effects.TestingClearSocialNotices();
            _eye.FireMiracle(eye, NeoTheologyMiracle.Alert);
            Assert.That(_effects.TestingGetSocialNotices(preacher), Has.Some.Contains("evil presence"));
            Assert.That(_effects.TestingGetSocialNotices(disciple), Is.Empty);
        });
    }

    [Test]
    public async Task EyeOddityCanOnlyBeReleasedOnce()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = Powered("OxydNtEyeOfTheProtector", map.GridCoords);
            var count = SEntMan.EntityQuery<NeoTheologySealComponent>().Count();
            _eye.FireMiracle(eye, NeoTheologyMiracle.Oddity);
            _eye.FireMiracle(eye, NeoTheologyMiracle.Oddity);
            Assert.That(SEntMan.EntityQuery<NeoTheologySealComponent>().Count(), Is.EqualTo(count + 1));
            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).OddityReleased, Is.True);
        });
    }

    [Test]
    public async Task ObeliskShortensPersonalCooldownsWithoutMovingThemIntoThePast()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords);
            var bearer = SComp<CruciformBearerComponent>(body);
            bearer.PersonalCooldowns["Long"] = _timing.CurTime + TimeSpan.FromSeconds(10);
            bearer.PersonalCooldowns["Short"] = _timing.CurTime + TimeSpan.FromSeconds(1);
            var obelisk = Powered("OxydNtObelisk", map.GridCoords);
            _obelisk.Tick(obelisk);
            Assert.That(bearer.PersonalCooldowns["Long"], Is.EqualTo(_timing.CurTime + TimeSpan.FromSeconds(7)));
            Assert.That(bearer.PersonalCooldowns["Short"], Is.EqualTo(_timing.CurTime));
        });
    }

    [Test]
    public async Task ReactorProcessesADeadBodyOnlyOnceAndPreservesItsSoul()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = Powered("OxydNtBioreactor", map.GridCoords);
            SComp<BioreactorComponent>(machine).ChamberSolution = true;
            var body = Bearer(SComp<TransformComponent>(machine).Coordinates);
            var implant = Implant(body);
            _mob.ChangeMobState(body, MobState.Dead);
            Assert.That(_reactor.TryProcessBody(machine, body), Is.True);
            Assert.That(_reactor.TryProcessBody(machine, body), Is.False);
            Assert.That(SEntMan.IsQueuedForDeletion(body), Is.True);
            Assert.That(SEntMan.IsQueuedForDeletion(implant), Is.False);
            Assert.That(SComp<CruciformComponent>(implant).ImplantedEntity, Is.Null);
            Assert.That(SComp<CruciformSoulComponent>(implant).HasSnapshot, Is.True);
        });
    }

    [Test]
    public async Task ArmoryCapacityIncreasesOnEachProductsFirstPurchaseOnly()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = Powered("OxydNtEyeOfTheProtector", map.GridCoords);
            var printer = Powered("OxydNtArmamentsPrinter", map.GridCoords);
            var buyer = Bearer(map.GridCoords);
            var state = SComp<EyeOfTheProtectorComponent>(eye);
            var maximum = state.MaxArmamentsPoints;
            state.ArmamentsPoints = 10000;
            Assert.That(_printer.TryPurchase(printer, buyer, "OxydNtArmamentThemis"), Is.True);
            Assert.That(_printer.TryPurchase(printer, buyer, "OxydNtArmamentThemis"), Is.True);
            Assert.That(_printer.TryPurchase(printer, buyer, "OxydNtArmamentPurger"), Is.True);
            Assert.That(state.MaxArmamentsPoints, Is.EqualTo(maximum + 50));
        });
    }

    [Test]
    public async Task ObeyKitInstallsANativeMindRoleAndUninstallRevokesIt()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SSpawnAtPosition("MobHuman", map.GridCoords);
            var mind = _minds.CreateMind(null, "Bound soul").Owner;
            _minds.TransferTo(mind, body);
            var implant = _implants.AddImplant(body, "OxydNtCruciform")!.Value;
            var comp = SComp<CruciformComponent>(implant);
            var kit = SSpawnAtPosition("OxydNtObeyKit", map.GridCoords);
            Assert.That(_upgrades.TryInstallCoreUpgrade(implant, comp, kit, body), Is.True);
            Assert.That(_cruciform.Activate(body), Is.True);
            Assert.That(_roles.MindHasRole<NeoTheologyObeyRoleComponent>(mind), Is.True);
            Assert.That(_upgrades.TryRemoveCoreUpgrades(implant, comp), Is.True);
            Assert.That(_roles.MindHasRole<NeoTheologyObeyRoleComponent>(mind), Is.False);
        });
    }

    [Test]
    public async Task DeactivationRevokesTheUplinkAndBanksItsBalance()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords, "OxydNtInquisitor");
            var implant = Implant(body);
            Assert.That(_uplink.TryGetUplink(implant, out _), Is.True);
            var mind = _minds.CreateMind(null, "Inquisitor").Owner;
            _minds.TransferTo(mind, body);
            Assert.That(_uplink.TryGetUplink(implant, out var uplink), Is.True);
            var store = _uplink.GetOrCreateStore(body, implant, uplink!);
            Assert.That(_cruciform.Deactivate(body), Is.True);
            Assert.That(_uplink.TryGetUplink(implant, out _), Is.False);
            Assert.That(SEntMan.IsQueuedForDeletion(store), Is.True);
            Assert.That(SComp<NtUplinkComponent>(implant).StoredTelecrystals.Float(), Is.EqualTo(15f));
        });
    }

    [Test]
    public async Task SanctificationCompletesNativeObjectiveAndCrusadeActivatesTheSword()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var waypoint = SSpawnAtPosition(null, map.GridCoords);
            SEntMan.EnsureComponent<WarpPointComponent>(waypoint);
            var body = Bearer(map.GridCoords, "OxydNtPreacher");
            var mind = _minds.CreateMind(null, "Preacher").Owner;
            _minds.TransferTo(mind, body);
            var objective = SComp<MindComponent>(mind).Objectives.Single(o =>
                SEntMan.TryGetComponent<NeoTheologyObjectiveComponent>(o, out var comp) &&
                comp.Kind == NeoTheologyObjectiveKind.Sanctify);
            Assert.That(_world.Sanctify(body), Is.True);
            Assert.That(SComp<NeoTheologyObjectiveComponent>(objective).Completed, Is.True);
            Assert.That(SComp<NeoTheologySanctifiedAreaComponent>(map.GridCoords.EntityId).Tiles, Is.Not.Empty);
            var sword = SSpawnAtPosition("OxydNtSwordOfTruth", map.GridCoords);
            var crusade = new NeoTheologyCrusadeEvent();
            SEntMan.EventBus.RaiseLocalEvent(body, ref crusade, broadcast: true);
            Assert.That(SComp<NeoTheologyFactionItemComponent>(sword).CrusadeActivated, Is.True);
        });
    }

    [Test]
    public async Task SwordDestructionPaysOnlyOnceAndCompletesItsOwnersObjective()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords, "OxydNtPreacher");
            var mind = _minds.CreateMind(null, "Preacher").Owner;
            _minds.TransferTo(mind, body);
            var enemy = SSpawnAtPosition("GoldRing", map.GridCoords);
            SEntMan.EnsureComponent<NeoTheologyFactionItemComponent>(enemy).Church = false;
            _world.AssignObjectives(body);
            var sword = SSpawnAtPosition("OxydNtSwordOfTruth", map.GridCoords);
            Assert.That(_hands.TryPickupAnyHand(body, sword), Is.True);
            var eye = Powered("OxydNtEyeOfTheProtector", map.GridCoords);
            Assert.That(_artifacts.TryDestroyArtifact(sword, body, enemy), Is.True);
            Assert.That(_artifacts.TryDestroyArtifact(sword, body, enemy), Is.False);
            Assert.That(SComp<EyeOfTheProtectorComponent>(eye).Observation, Is.EqualTo(200));
            Assert.That(SComp<MindComponent>(mind).Objectives.Any(o =>
                SEntMan.TryGetComponent<NeoTheologyObjectiveComponent>(o, out var objective) &&
                objective.Kind == NeoTheologyObjectiveKind.Destroy && objective.Completed), Is.True);
        });
    }

    [Test]
    public async Task LastShelterRecoversALooseSoulButCannotStealAnImplantedOne()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var living = Bearer(map.GridCoords);
            var mind = _minds.CreateMind(null, "Lost disciple").Owner;
            _minds.TransferTo(mind, living);
            var implant = Implant(living);
            var rescuer = SSpawnAtPosition("MobHuman", map.GridCoords);
            var shelter = SSpawnAtPosition("OxydNtLastShelter", map.GridCoords);
            Assert.That(_hands.TryPickupAnyHand(rescuer, shelter), Is.True);
            Assert.That(_artifacts.TryRecover(shelter, rescuer, out _), Is.False);
            _mob.ChangeMobState(living, MobState.Dead);
            Assert.That(_foundation.TryHardEject(living), Is.True);
            SComp<LastShelterComponent>(shelter).NextRecovery = TimeSpan.Zero;
            Assert.That(_artifacts.TryRecover(shelter, rescuer, out var recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(implant));
            Assert.That(_artifacts.TryRecover(shelter, rescuer, out _), Is.False);
        });
    }

    private EntityUid Bearer(EntityCoordinates coords, string profile = "OxydNtDisciple")
    {
        var body = SSpawnAtPosition("MobHuman", coords);
        Assert.That(_cruciform.GrantCruciform(body, profile), Is.True);
        return body;
    }

    private EntityUid Implant(EntityUid body)
    {
        Assert.That(_cruciform.TryGetCruciformEntity(body, out var implant, out _), Is.True);
        return implant;
    }

    private EntityUid Powered(string prototype, EntityCoordinates coords)
    {
        var entity = SSpawnAtPosition(prototype, coords);
        var receiver = SComp<ApcPowerReceiverComponent>(entity);
        receiver.NeedsPower = false;
        receiver.Powered = true;
        return entity;
    }
}
