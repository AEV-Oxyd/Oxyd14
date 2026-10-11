using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Shared.Nutrition.Components;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Botany.Components;
using Content.Shared.Doors;
using Content.Shared.Power;
using Content.Shared.Power.Generator;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Metabolism;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>Regressions for the PR review, using normal grants and native content systems.</summary>
public sealed class LitanyReviewFixTest : SocialNoticeGameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, DummyTicker = false };
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _transform = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;
    [SidedDependency(Side.Server)] private readonly IRobustRandom _random = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _materials = default!;
    [SidedDependency(Side.Server)] private readonly BiomatterReservoirSystem _reservoir = default!;
    [SidedDependency(Side.Server)] private readonly BiogeneratorSystem _generator = default!;
    [SidedDependency(Side.Server)] private readonly ScryingSystem _scrying = default!;

    private EntityUid Grant(EntityCoordinates coords, string role = "Disciple")
    {
        var body = SSpawnAtPosition("MobHumanOxyd", coords);
        // Vacuum/hunger are not the cause under test when moving patients or waiting out chants.
        SEntMan.RemoveComponent<BarotraumaComponent>(body);
        SEntMan.RemoveComponent<RespiratorComponent>(body);
        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
        Assert.That(_cruciform.GrantCruciform(body, $"OxydNt{role}"), Is.True);
        _litany.TestingTreatAsActor(body);
        return body;
    }

    [TestCase("Disciple", 50, NeoTheologyClearance.None)]
    [TestCase("Acolyte", 50, NeoTheologyClearance.None)]
    [TestCase("Agrolyte", 50, NeoTheologyClearance.None)]
    [TestCase("Custodian", 50, NeoTheologyClearance.None)]
    [TestCase("Preacher", 80, NeoTheologyClearance.Clergy)]
    [TestCase("Inquisitor", 100, NeoTheologyClearance.Clergy)]
    public async Task NormalGrantsSupplySoulModulesCorrectCapacityAndClearance(string role, int capacity, NeoTheologyClearance clearance)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Grant(map.GridCoords);
            // Re-grants reject rather than replace an existing saved identity.
            Assert.That(_cruciform.GrantCruciform(body, $"OxydNt{role}"), Is.False);
            var ranked = Grant(map.GridCoords.Offset(Vector2.UnitX), role);
            Assert.That(_cruciform.TryGetCruciform(ranked, out var implant, out var comp), Is.True);
            Assert.That(comp.InstalledModules, Does.Contain((ProtoId<CoreModulePrototype>) "OxydNtModuleCloning"));
            var soul = SComp<CruciformSoulComponent>(implant);
            Assert.That(soul.HasSnapshot, Is.True);
            Assert.That(soul.Dna, Is.Not.Empty);
            Assert.That(comp.MaxHoliness, Is.EqualTo(capacity));
            Assert.That(comp.Clearance, Is.EqualTo(clearance));
            Assert.That(comp.InstalledModules.Contains("OxydNtModuleRedLight"), Is.EqualTo(role == "Preacher"));
        });
    }

    [TestCase("Preacher", "Inquisitor", 80)]
    [TestCase("Inquisitor", "Preacher", 80)]
    public async Task RankTransitionsClampOnlyToTheFinalCapacity(string from, string to, int remaining)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Grant(map.GridCoords, from);
            Assert.That(_cruciform.TrySetProfile(body, $"OxydNt{to}"), Is.True);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(remaining));
        });
    }

    [TestCase("Acolyte")]
    [TestCase("Agrolyte")]
    [TestCase("Custodian")]
    public async Task RemovedSpecializationStaysRevokedAfterRecompute(string role)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Grant(map.GridCoords, role);
            var remove = new LitanyRemoveSpecializationEvent(body, false);
            SEntMan.EventBus.RaiseLocalEvent(body, ref remove);
            Assert.That(remove.Handled, Is.True);
            _cruciform.TryGetCruciform(body, out var implant, out var comp);
            _cruciform.RecomputeProfile(implant, comp);
            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtDisciple"));
            Assert.That(comp.UnlockedSets, Does.Not.Contain((ProtoId<LitanySetPrototype>) $"OxydLitany{role}"));
        });
    }

    [TestCase("range")]
    [TestCase("implant")]
    public async Task PendingFollowerCastRevalidatesBeforePayment(string change)
    {
        var map = await Pair.CreateMachineTestMap();
        EntityUid caster = default;
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearActors();
            caster = Grant(map.GridCoords.Offset(new Vector2(0.5f, 0.5f)), "Inquisitor");
            var target = Grant(map.GridCoords.Offset(new Vector2(1.5f, 0.5f)));
            var begin = _litany.TryBeginLitany(caster, "OxydLitanyPenance", LitanyCastOrigin.ManualSpeech,
                spokenName: SComp<MetaDataComponent>(target).EntityName);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id);
            if (change == "range")
                _transform.SetCoordinates(target, map.GridCoords.Offset(new Vector2(20f, 0.5f)));
            else
            {
                var implant = SComp<CruciformBearerComponent>(target).Cruciform!.Value;
                Assert.That(_effects.TryExtractInstalledCruciform(target, implant), Is.True);
            }
        });
        await Pair.RunSeconds(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingPendingCount, Is.Zero);
            Assert.That(_cruciform.GetHoliness(caster), Is.EqualTo(100), "Invalidated targets must not charge the caster.");
        });
    }

    [Test]
    public async Task RevealIsCasterCenteredAndDoesNotWarnForDeadFauna()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var caster = Grant(map.GridCoords);
            var carp = SSpawnAtPosition("MobCarp", map.GridCoords.Offset(Vector2.UnitX));
            Assert.That(_prototypes.Index<LitanyPrototype>("OxydLitanyRevealAdversaries").TargetMode, Is.EqualTo(LitanyTargetMode.Self));
            _effects.TestingClearSocialNotices();
            _random.SetSeed(42); // Above the source's hidden 20% failure on this first roll.
            var reveal = new LitanyRevealAdversariesEvent(caster, false);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref reveal);
            Assert.That(_effects.TestingGetSocialNotices(caster), Is.Not.Empty);
            var aliveNotice = _effects.TestingGetSocialNotices(caster).Single();
            _mobState.ChangeMobState(carp, MobState.Dead);
            _effects.TestingClearSocialNotices();
            _random.SetSeed(42);
            reveal = new LitanyRevealAdversariesEvent(caster, false);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref reveal);
            Assert.That(_effects.TestingGetSocialNotices(caster).Single(), Is.Not.EqualTo(aliveNotice));
        });
    }

    [Test]
    public async Task GrowthValidationDoesNotMutateAndNeedsNoOtherMob()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var caster = Grant(map.GridCoords, "Agrolyte");
            var plant = SSpawnAtPosition("WheatPlants", map.GridCoords.Offset(Vector2.UnitX));
            var growth = SEntMan.EnsureComponent<PlantGrowthComponent>(plant);
            var validate = new LitanyAcceleratedGrowthEvent(caster, 2f, TimeSpan.FromMinutes(5), false, ValidateOnly: true);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref validate);
            Assert.That(validate.Handled, Is.True);
            Assert.That(growth.GrowthMultiplier, Is.EqualTo(1f));
            var apply = new LitanyAcceleratedGrowthEvent(caster, 2f, TimeSpan.FromMinutes(5), false);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref apply);
            Assert.That(apply.Handled, Is.True);
            Assert.That(growth.GrowthMultiplier, Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task PublicDoorsAllowUnimplantedUsersWhileClergyDoorsRequireClearance()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var ordinary = SSpawnAtPosition("MobHumanOxyd", map.GridCoords);
            var publicDoor = SSpawnAtPosition("OxydNtHolyDoorPublic", map.GridCoords.Offset(Vector2.UnitX));
            var clergyDoor = SSpawnAtPosition("OxydNtHolyDoorClergy", map.GridCoords.Offset(-Vector2.UnitX));
            // Access is under test, not an airlock's independent no-power veto.
            var power = new PowerChangedEvent(true, 1000f);
            SEntMan.EventBus.RaiseLocalEvent(publicDoor, ref power);
            SEntMan.EventBus.RaiseLocalEvent(clergyDoor, ref power);
            var open = new BeforeDoorOpenedEvent { User = ordinary };
            SEntMan.EventBus.RaiseLocalEvent(publicDoor, open);
            Assert.That(open.Cancelled, Is.False);
            open = new BeforeDoorOpenedEvent { User = ordinary };
            SEntMan.EventBus.RaiseLocalEvent(clergyDoor, open);
            Assert.That(open.Cancelled, Is.True);
            var preacher = Grant(map.GridCoords, "Preacher");
            open = new BeforeDoorOpenedEvent { User = preacher };
            SEntMan.EventBus.RaiseLocalEvent(clergyDoor, open);
            Assert.That(open.Cancelled, Is.False);
        });
    }

    [Test]
    public async Task ScryingFollowsTargetAndEndsOnTargetDeletion()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        await Server.WaitAssertion(() =>
        {
            caster = SSpawnAtPosition(null, map.GridCoords);
            SEntMan.EnsureComponent<EyeComponent>(caster);
            var target = SSpawnAtPosition(null, map.GridCoords);
            Assert.That(_scrying.TryStartSession(caster, target, TimeSpan.FromSeconds(30)), Is.True);
            var marker = SComp<ScryingSessionComponent>(caster).Marker!.Value;
            Assert.That(SComp<TransformComponent>(marker).Coordinates.EntityId, Is.EqualTo(target));
            _transform.SetCoordinates(target, map.GridCoords.Offset(new Vector2(5f, 3f)));
            Assert.That(SComp<TransformComponent>(marker).WorldPosition, Is.EqualTo(SComp<TransformComponent>(target).WorldPosition));
            SEntMan.QueueDeleteEntity(target);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.False));
    }

    [Test]
    public async Task UprootCannotRefundTheSameQueuedConstructTwice()
    {
        var map = await Pair.CreateMachineTestMap();
        EntityUid caster = default;
        EntityUid construct = default;
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            caster = Grant(origin, "Custodian");
            _transform.SetLocalRotation(caster, Angle.Zero);
            construct = SSpawnAtPosition("OxydNtAltar", origin.Offset(Vector2.UnitX));
        });
        await Pair.RunTicksSync(2); // Let the anchored construct enter the physics lookup.
        await Server.WaitAssertion(() =>
        {
            var first = new LitanyUprootEvent(caster, false, false);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref first, broadcast: true);
            Assert.That(first.Handled, Is.True, first.Failure?.Id);
            Assert.That(SEntMan.IsQueuedForDeletion(construct), Is.True);
            var second = new LitanyUprootEvent(caster, false, false);
            SEntMan.EventBus.RaiseLocalEvent(caster, ref second, broadcast: true);
            Assert.That(second.Handled, Is.False);
        });
    }

    [Test]
    public async Task UnpaidGeneratorOutputStopsAndReservoirTransfersConserveBiomatter()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var generator = SSpawnAtPosition("OxydNtBiogenerator", map.GridCoords);
            Assert.That(_materials.TryChangeMaterialAmount(generator, "Biomatter", 1), Is.True);
            Assert.That(_generator.TryToggle(generator), Is.True);
            var fuel = SComp<FuelGeneratorComponent>(generator);
            Assert.That(fuel.On, Is.True);
            // The stock generator burns the single unit, then shuts itself down on empty.
            SEntMan.System<GeneratorSystem>().Update(1f);
            SEntMan.System<GeneratorSystem>().Update(1f);
            Assert.That(fuel.On, Is.False);
            Assert.That(SComp<PowerSupplierComponent>(generator).Enabled, Is.False);
            Assert.That(_materials.GetMaterialAmount(generator, "Biomatter"), Is.Zero);
            var canister = SSpawnAtPosition("OxydNtBiomatterCanister", map.GridCoords);
            var printer = SSpawnAtPosition("OxydNtBioprinter", map.GridCoords.Offset(Vector2.UnitX));
            Assert.That(_materials.TryChangeMaterialAmount(canister, "Biomatter", 100), Is.True);
            Assert.That(_reservoir.TryTransfer(canister, printer), Is.True);
            Assert.That(_materials.GetMaterialAmount(canister, "Biomatter"), Is.Zero);
            Assert.That(_materials.GetMaterialAmount(printer, "Biomatter"), Is.EqualTo(100));
        });
    }

    [Test]
    public async Task CoreAscensionKitIsRequiredAndRemovalPreservesPhysicalAttachment()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var target = Grant(map.GridCoords);
            Assert.That(_cruciform.TryGetCruciform(target, out var implant, out var comp), Is.True);
            var initiation = new LitanyInitiationEvent(target, false);
            SEntMan.EventBus.RaiseLocalEvent(target, ref initiation);
            Assert.That(initiation.Handled, Is.False, "Initiation cannot conjure a kit.");
            var kit = SSpawnAtPosition("OxydNtPreacherAscensionKit", map.GridCoords);
            Assert.That(_upgrades.TryInstallCoreUpgrade(implant, comp, kit), Is.True);
            initiation = new LitanyInitiationEvent(target, false);
            SEntMan.EventBus.RaiseLocalEvent(target, ref initiation);
            Assert.That(initiation.Handled, Is.True);
            var physical = SSpawnAtPosition("OxydNtUpgradeFaithsShield", map.GridCoords);
            Assert.That(_upgrades.TryInstallUpgrade(implant, comp, physical), Is.True);
            Assert.That(_upgrades.TryRemoveCoreUpgrades(implant, comp), Is.True);
            Assert.That(comp.Profile.Id, Is.EqualTo("OxydNtDisciple"));
            Assert.That(comp.Upgrade, Is.EqualTo(physical));
            Assert.That(comp.InstalledModules, Does.Not.Contain((ProtoId<CoreModulePrototype>) "OxydNtModulePriestConvert"));
        });
    }

    [Test]
    public async Task RegenerationUsesLiveFaithfulRighteousnessAndMetabolismInputs()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var caster = Grant(map.GridCoords, "Preacher");
            Assert.That(_cruciform.TryGetCruciform(caster, out _, out var comp), Is.True);
            var before = _cruciform.GetRegenerationPerSecond(caster);
            Grant(map.GridCoords.Offset(Vector2.UnitX));
            Assert.That(_cruciform.GetRegenerationPerSecond(caster), Is.GreaterThan(before));
            comp.RighteousLife = 100;
            Assert.That(_cruciform.GetRegenerationPerSecond(caster), Is.GreaterThan(before));
            var drink = new ReagentMetabolizedEvent(_prototypes.Index<ReagentPrototype>("Beer"), "Digestion");
            SEntMan.EventBus.RaiseLocalEvent(caster, ref drink);
            Assert.That(comp.RighteousLife, Is.EqualTo(99.9f).Within(0.0001f));
            var cahors = new ReagentMetabolizedEvent(_prototypes.Index<ReagentPrototype>("NTCahors"), "Digestion");
            SEntMan.EventBus.RaiseLocalEvent(caster, ref cahors);
            Assert.That(comp.RighteousLife, Is.EqualTo(99.9f).Within(0.0001f));
            var drug = new ReagentMetabolizedEvent(_prototypes.Index<ReagentPrototype>("SpaceDrugs"), "Bloodstream");
            SEntMan.EventBus.RaiseLocalEvent(caster, ref drug);
            Assert.That(comp.RighteousLife, Is.EqualTo(99.4f).Within(0.0001f));
        });
    }

    [Test]
    public async Task BookBlessingAcceptsOtherHeldOddityAndCooldownSnapshotExpires()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var caster = Grant(map.GridCoords, "Preacher");
            var hands = SComp<HandsComponent>(caster);
            var book = SSpawnAtPosition("OxydNtBible", map.GridCoords);
            var oddity = SSpawnAtPosition("Crowbar", map.GridCoords);
            SEntMan.AddComponent<OddityComponent>(oddity).giving["Cog"] = 1;
            Assert.That(_hands.TryPickup(caster, book, hands.ActiveHandId!), Is.True);
            var otherHand = hands.SortedHands.First(hand => hand != hands.ActiveHandId);
            Assert.That(_hands.TryPickup(caster, oddity, otherHand), Is.True);
            Assert.That(_effects.TryGetHeldOddity(caster, out var selected, out _), Is.True);
            Assert.That(selected, Is.EqualTo(oddity));
            var begin = _litany.TryBeginLitany(caster, "OxydLitanyDivineBlessing", LitanyCastOrigin.Book, book: book);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id);
            var follower = SComp<CruciformBearerComponent>(caster);
            var litany = _prototypes.Index<LitanyPrototype>("OxydLitanySuccour");
            follower.PersonalCooldowns[litany.CooldownKey] = _timing.CurTime + TimeSpan.FromSeconds(10);
            // Use an inquisitor for an entitled cooldown snapshot, rather than a priest-only entry.
            var inquisitor = Grant(map.GridCoords.Offset(Vector2.UnitX), "Inquisitor");
            SComp<CruciformBearerComponent>(inquisitor).PersonalCooldowns[litany.CooldownKey] = follower.PersonalCooldowns[litany.CooldownKey];
            var entry = _litany.TestingBuildViewerSnapshot(inquisitor).Entries.Single(e => e.Litany == litany.ID);
            Assert.That(entry.Available, Is.False);
            Assert.That(entry.CooldownEndsAt, Is.GreaterThan(_timing.CurTime));
            SComp<CruciformBearerComponent>(inquisitor).PersonalCooldowns[litany.CooldownKey] = _timing.CurTime;
            Assert.That(_litany.TestingBuildViewerSnapshot(inquisitor).Entries.Single(e => e.Litany == litany.ID).Available, Is.True);
        });
    }
}
