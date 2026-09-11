using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Server.Power.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.13: the obelisk's aura. The tests drive <see cref="ObeliskSystem.Tick"/> directly rather than
/// waiting out the one-second view cadence. The obelisk entity prototype only lands in P2.14, so these
/// tests build a bare entity and attach the component themselves.
/// </summary>
[TestOf(typeof(ObeliskSystem))]
public sealed class ObeliskTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId HostileProto = "MobCarp";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly ObeliskSystem _obelisk = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;

    [Test]
    public async Task AuraActivatesOnAFaithfulInRangeAndDoublesTheirRegeneration()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var obelisk = SpawnObelisk(map.GridCoords);
            var body = ActiveBearer(map.GridCoords);
            var unhurried = _cruciform.GetRegenerationPerSecond(body);

            Assert.That(unhurried, Is.GreaterThan(0),
                "Setup: a faithful with no aura must already regenerate.");

            _obelisk.Tick(obelisk);

            Assert.Multiple(() =>
            {
                Assert.That(SComp<ObeliskComponent>(obelisk).Active, Is.True,
                    "A faithful in range must switch the aura on.");
                Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(unhurried * 2).Within(1e-6),
                    "The faithful must regenerate at double rate inside the aura.");
            });
        });
    }

    [Test]
    public async Task HostileMobInRangeTakesDamageAndDiesWithinTheRadiusInTicks()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var obelisk = SpawnObelisk(map.GridCoords);
            var obeliskComp = SComp<ObeliskComponent>(obelisk);
            ActiveBearer(map.GridCoords);
            var mob = SSpawnAtPosition(HostileProto, map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(SComp<MobStateComponent>(mob), Is.Not.Null, "Setup: the target must be a mob.");
                Assert.That(SEntMan.HasComponent<NpcFactionMemberComponent>(mob), Is.True,
                    "Setup: the aura treats a simple mob as hostile; crew are never targeted.");
            });

            for (var i = 0; i < obeliskComp.Radius && _mobState.IsAlive(mob); i++)
                _obelisk.Tick(obelisk);

            Assert.That(_mobState.IsDead(mob), Is.True,
                "A hostile in range must take lethal damage within a handful of aura ticks.");
        });
    }

    [Test]
    public async Task AuraStopsOutsideTheRadius()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var obelisk = SpawnObelisk(map.GridCoords);

            var obeliskComp = SComp<ObeliskComponent>(obelisk);
            var outside = map.GridCoords.Offset(new Vector2(obeliskComp.Radius * 3f, 0f));

            var body = ActiveBearer(outside);
            var unhurried = _cruciform.GetRegenerationPerSecond(body);
            var mob = SSpawnAtPosition(HostileProto, outside);

            _obelisk.Tick(obelisk);

            Assert.Multiple(() =>
            {
                Assert.That(obeliskComp.Active, Is.False,
                    "No faithful in range means no aura.");
                Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(unhurried).Within(1e-6),
                    "A faithful outside the radius must not be buffed.");
                Assert.That(_mobState.IsAlive(mob), Is.True,
                    "A mob outside the radius must be untouched.");
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewAndFriendlyAnimalsNeverTakeAuraDamage(bool active)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var obelisk = SpawnObelisk(map.GridCoords);
            if (active)
                ActiveBearer(map.GridCoords);
            var human = SSpawnAtPosition(HumanProto, map.GridCoords);
            var mouse = SSpawnAtPosition("MobMouse", map.GridCoords);
            var carp = SSpawnAtPosition(HostileProto, map.GridCoords);
            _obelisk.Tick(obelisk);
            Assert.That(DamageOf(human), Is.EqualTo(0));
            Assert.That(DamageOf(mouse), Is.EqualTo(0));
            Assert.That(DamageOf(carp),
                active ? Is.GreaterThan(0) : Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AuraRecomputesAcrossMovementMapsOverlapAndRemoval()
    {
        var map = await Pair.CreateTestMap();
        var otherMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var first = SpawnObelisk(map.GridCoords);
            var second = SpawnObelisk(map.GridCoords.Offset(new Vector2(30f, 0f)));
            var body = ActiveBearer(map.GridCoords);
            var normal = _cruciform.GetRegenerationPerSecond(body);
            var transform = SEntMan.System<SharedTransformSystem>();
            _obelisk.Tick(first);
            _obelisk.Tick(second);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal * 2));

            transform.SetCoordinates(body, map.GridCoords.Offset(new Vector2(15f, 0f)));
            _obelisk.Tick(first);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal));
            transform.SetCoordinates(body, map.GridCoords);
            _obelisk.Tick(first);
            transform.SetCoordinates(body, otherMap.GridCoords);
            _obelisk.Tick(first);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal));

            transform.SetCoordinates(body, map.GridCoords);
            transform.SetCoordinates(second, map.GridCoords);
            SComp<ObeliskComponent>(second).RegenMultiplier = 3;
            _obelisk.Tick(first);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal * 3));
            SEntMan.DeleteEntity(second);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal * 2));
            SEntMan.RemoveComponent<ObeliskComponent>(first);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal));
        });
    }

    [Test]
    public async Task PowerLossStopsDamageAndResetsRegeneration()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var obelisk = SSpawnAtPosition("OxydNtObelisk", map.GridCoords);
            var receiver = SComp<ApcPowerReceiverComponent>(obelisk);
            Assert.That(SComp<TransformComponent>(obelisk).Anchored, Is.True);
            receiver.Powered = true;
            var body = ActiveBearer(map.GridCoords);
            var normal = _cruciform.GetRegenerationPerSecond(body);
            _obelisk.Tick(obelisk);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal * 2));

            receiver.Powered = false;
            var carp = SSpawnAtPosition(HostileProto, map.GridCoords);
            _obelisk.Tick(obelisk);
            Assert.That(DamageOf(carp), Is.EqualTo(0));
            Assert.That(SComp<ObeliskComponent>(obelisk).Active, Is.False);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal));

            receiver.Powered = true;
            SEntMan.System<SharedTransformSystem>().Unanchor(obelisk);
            _obelisk.Tick(obelisk);
            Assert.That(SComp<ObeliskComponent>(obelisk).Active, Is.False);
            Assert.That(_cruciform.GetRegenerationPerSecond(body), Is.EqualTo(normal));
        });
    }

    private float DamageOf(EntityUid uid) => (float) SEntMan.System<DamageableSystem>()
        .GetPositiveDamage((uid, SComp<DamageableComponent>(uid))).GetTotal();

    private EntityUid SpawnObelisk(EntityCoordinates coords)
    {
        // ponytail: the prototype arrives in P2.14; until then the aura is exercised on a bare entity.
        var obelisk = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<ObeliskComponent>(obelisk);
        return obelisk;
    }

    private EntityUid ActiveBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: a cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be active.");
        return body;
    }
}
