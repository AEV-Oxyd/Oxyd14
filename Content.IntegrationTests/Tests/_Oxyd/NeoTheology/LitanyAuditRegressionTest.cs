using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Utility;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.Framework.ViewCalc;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Store;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

public sealed class LitanyAuditRegressionTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          parent: WheatPlants
          id: TestNtFragilePlant
          components:
          - type: Plant
            endurance: 10
        """;

    public override PoolSettings PoolSettings => new() { Connected = false, DummyTicker = false };
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly CruciformUpgradeSystem _upgrades = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _containers = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damage = default!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _transform = default!;
    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly NtUplinkSystem _uplink = default!;
    [SidedDependency(Side.Server)] private readonly UserInterfaceSystem _ui = default!;

    private EntityUid Bearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition("MobHuman", coords);
        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
        SEntMan.RemoveComponent<BarotraumaComponent>(body);
        Assert.That(_implants.AddImplant(body, "OxydNtCruciform"), Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        return body;
    }

    private void Install(EntityUid body, string prototype)
    {
        Assert.That(_cruciform.TryGetCruciform(body, out var implant, out var comp), Is.True);
        var item = SSpawnAtPosition(prototype, SComp<TransformComponent>(body).Coordinates);
        Assert.That(_upgrades.TryInstallUpgrade(implant, comp, item), Is.True);
    }

    private void Extract(EntityUid body, EntityUid implant)
    {
        Assert.That(_containers.Remove(implant, SComp<ImplantedComponent>(body).ImplantContainer), Is.True);
        var removed = new ImplantRemovedEvent(implant, body);
        SEntMan.EventBus.RaiseLocalEvent(implant, ref removed);
    }

    [Test]
    public async Task ExtractedSpeedUpgradeRestoresSpeed()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = Bearer(map.GridCoords);
            var movement = SComp<MovementSpeedModifierComponent>(body);
            var speed = movement.CurrentWalkSpeed;
            Install(body, "OxydNtUpgradeSpeedOfTheChosen");
            Assert.That(movement.CurrentWalkSpeed, Is.GreaterThan(speed));
            Assert.That(_cruciform.TryGetCruciform(body, out var implant, out _), Is.True);
            Extract(body, implant);
            Assert.That(_cruciform.IsActiveBearer(body), Is.False);
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(speed).Within(0.01f));
        });
    }

    [Test]
    public async Task NatureAuraHealsSlashDamage()
    {
        var map = await Pair.CreateTestMap();
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            var bearer = Bearer(map.GridCoords);
            target = Bearer(map.GridCoords.Offset(new Vector2(2, 0)));
            Install(bearer, "OxydNtUpgradeNaturesBlessing");
            _damage.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Slash"] = 60 } });
        });
        await RunSeconds(3f);
        await Server.WaitAssertion(() =>
            Assert.That(_damage.GetTotalDamage(target).Float(), Is.LessThan(60f)));
    }

    [Test]
    public async Task AuraUsesViewTargetsForHealingAndLookupForPlants()
    {
        var map = await Pair.CreateMachineTestMap();
        await Server.WaitAssertion(() =>
        {
            var bearer = Bearer(map.GridCoords);
            var visible = Bearer(map.GridCoords.Offset(new Vector2(2, 0)));
            var hidden = Bearer(map.GridCoords.Offset(new Vector2(2, 0)));
            var outside = Bearer(map.GridCoords.Offset(new Vector2(20, 0)));
            var tray = SSpawnAtPosition("HydroponicsTrayEmpty", map.GridCoords);
            var plant = SSpawnAtPosition("TestNtFragilePlant", map.GridCoords);
            var health = SComp<PlantHolderComponent>(plant);
            health.Health = 9.95f;
            Install(bearer, "OxydNtUpgradeNaturesBlessing");
            foreach (var target in new[] { visible, hidden, outside })
                _damage.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Slash"] = 60 } });
            SEntMan.System<PlantTraySystem>().AdjustWeed((tray, SComp<PlantTrayComponent>(tray)), 10);

            SEntMan.EventBus.RaiseLocalEvent(bearer, new ViewTickEvent { seen = [visible, outside] });
            Assert.That(_damage.GetTotalDamage(visible).Float(), Is.LessThan(60));
            Assert.That(_damage.GetTotalDamage(hidden).Float(), Is.EqualTo(60));
            Assert.That(_damage.GetTotalDamage(outside).Float(), Is.EqualTo(60));
            Assert.That(SComp<PlantTrayComponent>(tray).WeedLevel, Is.LessThan(10));
            Assert.That(health.Health, Is.EqualTo(SComp<PlantComponent>(plant).Endurance));
        });
    }

    [Test]
    public async Task StationaryViewTickerRefreshesTargetsAndRespectsWalls()
    {
        var map = await Pair.CreateMachineTestMap();
        EntityUid observer = default;
        EntityUid target = default;
        EntityUid wall = default;
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            observer = SSpawnAtPosition(null, origin);
            SEntMan.AddComponent<ViewTickerComponent>(observer);
            target = Bearer(origin.Offset(new Vector2(2, 0)));
            wall = SSpawnAtPosition("WallSolid", origin.Offset(new Vector2(1, 0)));
            var view = SEntMan.System<ViewCalcSystem>();
            Assert.That(SEntMan.HasComponent<ViewRelevantComponent>(target), Is.True);
            Assert.That(view.GetEntsInView(_transform.GetMapCoordinates(observer), 8), Does.Not.Contain(target));
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<ViewTickerComponent>(observer).lastSeen, Does.Not.Contain(target));
            SEntMan.DeleteEntity(wall);
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<ViewTickerComponent>(observer).lastSeen, Does.Contain(target));
            _transform.SetCoordinates(target, map.GridCoords.Offset(new Vector2(20, 0)));
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<ViewTickerComponent>(observer).lastSeen, Does.Not.Contain(target)));
    }

    [Test]
    public async Task NearbyBearerLookupExcludesOtherMapsInactiveAndBrokenLinks()
    {
        var map = await Pair.CreateTestMap();
        var otherMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var origin = SSpawnAtPosition(null, map.GridCoords);
            var nearby = Bearer(map.GridCoords.Offset(new Vector2(1, 0)));
            Bearer(map.GridCoords.Offset(new Vector2(7.1f, 0)));
            Bearer(otherMap.GridCoords);
            var inactive = SSpawnAtPosition("MobHuman", map.GridCoords);
            Assert.That(_implants.AddImplant(inactive, "OxydNtCruciform"), Is.Not.Null);
            var broken = Bearer(map.GridCoords);
            Assert.That(_cruciform.TryGetCruciform(broken, out _, out var state), Is.True);
            state.ImplantedEntity = null;

            Assert.That(_cruciform.BearersInRange(origin, 7).Select(entry => entry.Body), Is.EquivalentTo(new[] { nearby }));
            Assert.That(_cruciform.BearersInRange(origin, 0), Is.Empty);
        });
    }

    [Test]
    public async Task RotatedGridKeepsFrontTarget()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var coords = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            var caster = Bearer(coords);

            // The target must stand on a tile to stay parented to the grid and rotate with it.
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), new Tile(1));
            var target = Bearer(coords.Offset(new Vector2(1, 0)));
            var litany = _prototypes.Index<LitanyPrototype>("OxydLitanyConfirmation");
            Assert.That(_litany.TryResolveTargets(caster, litany, out var before, out _), Is.True);
            Assert.That(before, Does.Contain(target));
            _transform.SetLocalRotation(map.GridCoords.EntityId, Angle.FromDegrees(90));
            Assert.That(_litany.TryResolveTargets(caster, litany, out var after, out _), Is.True);
            Assert.That(after, Does.Contain(target));
        });
    }

    [Test]
    public async Task CompletedCastSendsIdleSnapshot()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        await Server.WaitAssertion(() =>
        {
            body = Bearer(map.GridCoords);
            _litany.TestingTreatAsActor(body);
            var book = SSpawnAtPosition("OxydNtBible", map.GridCoords);
            var hands = SEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
            Assert.That(hands.TryPickup(body, book), Is.True);
            Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);
            var result = _litany.TryBeginLitany(body, "OxydLitanyRelief", LitanyCastOrigin.Book,
                book: book, expectedRevision: SComp<CruciformBearerComponent>(body).UiRevision);
            Assert.That(result.Success, Is.True, result.Reason?.Id);
        });
        await RunSeconds(10f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingPendingCount, Is.Zero);
            Assert.That(_litany.TestingTryGetLastSnapshot(body, out var snapshot), Is.True);
            Assert.That(snapshot!.BusyState, Is.Null,
                "The final snapshot must not keep the book busy after completion.");
        });
    }

    [Test]
    public async Task ChoiceAcknowledgementKeepsCastProgress()
    {
        await Client.WaitAssertion(() =>
        {
            using var window = new Content.Client._Oxyd.NeoTheology.UI.LitanyWindow();
            window.UpdateProgress(new Content.Shared._Oxyd.NeoTheology.UI.LitanyProgressMessage(
                1, "audit-request", "OxydLitanySending", Content.Shared._Oxyd.NeoTheology.UI.LitanyCastStage.Chanting,
                TimeSpan.Zero, TimeSpan.FromSeconds(30), true));
            window.UpdateResult(new Content.Shared._Oxyd.NeoTheology.UI.LitanyResultMessage(
                1, LitanyActionResult.Ok("audit-request"), isFinal: false));
            var busy = typeof(Content.Client._Oxyd.NeoTheology.UI.LitanyWindow).GetField(
                "_busyState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(busy!.GetValue(window), Is.Not.Null,
                "The successful choice submission does not finish the cast.");

            window.UpdateResult(new Content.Shared._Oxyd.NeoTheology.UI.LitanyResultMessage(
                1, LitanyActionResult.Ok("audit-request"), isFinal: true));
            Assert.That(busy!.GetValue(window), Is.Null,
                "A terminal result finishes the cast.");
        });
    }

    [Test]
    public async Task ExtractionClosesUplink()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid store = default;
        await Server.WaitAssertion(() =>
        {
            body = Bearer(map.GridCoords);
            Assert.That(_cruciform.TryGetCruciform(body, out var implant, out var comp), Is.True);
            _cruciform.MakeInquisitor(implant, comp);
            Assert.That(_uplink.TryGetUplink(implant, out var uplink), Is.True);
            store = _uplink.GetOrCreateStore(body, implant, uplink!);
            _ui.OpenUi(store, StoreUiKey.Key, body);
            Assert.That(_ui.GetActors(store, StoreUiKey.Key), Does.Contain(body));
            Extract(body, implant);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
            Assert.That(_ui.GetActors(store, StoreUiKey.Key), Does.Not.Contain(body)));
    }
}
