using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Stacks;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.9: the bioreactor is Eris' platform/pump multistructure flattened into one machine, and its
/// three chamber booleans drive the pump, the platform door and crop processing.
/// </summary>
[TestOf(typeof(BioreactorSystem))]
public sealed class BioreactorTest : GameTest
{
    private static readonly EntProtoId CropProto = "FoodApple";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly BioreactorSystem _bioreactor = default!;

    [Test]
    public async Task PumpingAnOpenChamberFails()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var reactor = SpawnReactor(map.GridCoords);
            SComp<BioreactorComponent>(reactor).ChamberClosed = false;

            Assert.That(_bioreactor.TryPumpSolution(reactor), Is.False,
                "An open chamber cannot hold solution.");
        });
    }

    [Test]
    public async Task TogglingClearsAClearedBreachAndSucceeds()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var reactor = SpawnReactor(map.GridCoords);
            SComp<BioreactorComponent>(reactor).ChamberBreached = true;

            Assert.That(_bioreactor.TryToggleChamber(reactor), Is.True,
                "With nothing jamming the door the breach clears and the door toggles.");

            var comp = SComp<BioreactorComponent>(reactor);
            Assert.Multiple(() =>
            {
                Assert.That(comp.ChamberBreached, Is.False, "The cleared breach must be recorded.");
                Assert.That(comp.ChamberClosed, Is.False, "The door must have toggled.");
            });
        });
    }

    [Test]
    public async Task CropsLeftInTheChamberBecomeBiomatter()
    {
        var map = await Pair.CreateTestMap();
        EntityUid reactor = default;
        EntityUid crop = default;

        await Server.WaitAssertion(() =>
        {
            reactor = SpawnReactor(map.GridCoords);
            crop = SSpawnAtPosition(CropProto, map.GridCoords.Offset(new Vector2(1f, 0f)));
            Assert.That(SEntMan.HasComponent<ProduceComponent>(crop), Is.True,
                "Setup: the crop must be produce.");
        });

        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(crop), Is.True, "A dry chamber must not consume produce.");
            Assert.That(_bioreactor.TryPumpSolution(reactor), Is.True);
        });
        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(reactor).Coordinates;
            var biomatter = SEntMan.System<EntityLookupSystem>()
                .GetEntitiesInRange<StackComponent>(coords, 3f);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.EntityExists(crop), Is.False, "The crop must be consumed.");
                Assert.That(biomatter, Is.Not.Empty, "The crop must come back as biomatter.");
            });
        });
    }

    [Test]
    public async Task PowerAndValidationDoNotChangeTheChamberOrConsumeProduce()
    {
        var map = await Pair.CreateTestMap();
        EntityUid crop = default;
        await Server.WaitAssertion(() =>
        {
            var reactor = SSpawnAtPosition("OxydNtBioreactor", map.GridCoords);
            var comp = SComp<BioreactorComponent>(reactor);
            var receiver = SComp<ApcPowerReceiverComponent>(reactor);
            Assert.That(SComp<TransformComponent>(reactor).Anchored, Is.True);
            crop = SSpawnAtPosition(CropProto, map.GridCoords);
            receiver.Powered = true;
            var validation = new LitanyPumpBioreactorEvent(reactor, false, true);
            SEntMan.EventBus.RaiseLocalEvent(reactor, ref validation);
            Assert.That(validation.Handled, Is.True);
            Assert.That(comp.ChamberSolution, Is.False);
            Assert.That(_bioreactor.TryPumpSolution(reactor), Is.True);

            receiver.Powered = false;
            _bioreactor.Update(1);
            Assert.That(_bioreactor.TryPumpSolution(reactor), Is.False);
            Assert.That(comp.ChamberSolution, Is.True);
            Assert.That(_bioreactor.CanToggleChamber(reactor), Is.False);
            receiver.Powered = true;
            Assert.That(_bioreactor.CanToggleChamber(reactor), Is.False, "A filled chamber must not open.");
            comp.ChamberBreached = true;
            _bioreactor.Update(1);
            receiver.Powered = false;
        });
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(crop), Is.True));
    }

    private EntityUid SpawnReactor(EntityCoordinates coords)
    {
        var reactor = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<BioreactorComponent>(reactor);
        return reactor;
    }
}
