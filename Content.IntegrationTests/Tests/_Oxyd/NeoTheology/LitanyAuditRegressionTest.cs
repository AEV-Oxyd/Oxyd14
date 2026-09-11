using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
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
