using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.3: the ActivateDoor litany toggles the bolt on the holy NeoTheology door the
/// caster faces and refuses ordinary station doors.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsMachinesTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId HolyDoorProto = "OxydNtHolyDoor";
    private static readonly EntProtoId PlainDoorProto = "Airlock";
    private static readonly ProtoId<LitanyPrototype> ActivateDoor = "OxydLitanyActivateDoor";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task ActivateDoor_TogglesBoltOnFacingHolyDoor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid door = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            door = SpawnPoweredDoor(HolyDoorProto, origin.Offset(new Vector2(1f, 0f)));
            Assert.That(SComp<DoorBoltComponent>(door).BoltsDown, Is.False, "The door must start unbolted.");

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(ActivateDoor), out var targets, out var reason),
                Is.True, reason?.Id ?? "FrontMachine must resolve the faced door.");
            Assert.That(targets, Is.EqualTo(new[] { door }), "FrontMachine resolves the faced holy door only.");

            var begin = _litany.TryBeginLitany(caster, ActivateDoor, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "ActivateDoor begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<DoorBoltComponent>(door).BoltsDown, Is.True,
                "The first cast must bolt the faced door.");
            Assert.That(SComp<NeoTheologyDoorComponent>(door).LitanyLocked, Is.True,
                "LitanyLocked must mirror the bolt state.");
        });

        // Second cast toggles the bolt back off.
        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var begin = _litany.TryBeginLitany(caster, ActivateDoor, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "ActivateDoor second begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<DoorBoltComponent>(door).BoltsDown, Is.False,
                "The second cast must unbolt the faced door.");
            Assert.That(SComp<NeoTheologyDoorComponent>(door).LitanyLocked, Is.False);
        });
    }

    [Test]
    public async Task ActivateDoor_RefusesOrdinaryStationDoor()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            var plainDoor = SpawnPoweredDoor(PlainDoorProto, origin.Offset(new Vector2(1f, 0f)));

            var begin = _litany.TryBeginLitany(caster, ActivateDoor, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False, "An ordinary station door must never be a litany target.");
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(SComp<DoorBoltComponent>(plainDoor).BoltsDown, Is.False,
                "A refused cast must leave the plain door untouched.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>A human with an active cruciform.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        return body;
    }

    /// <summary>
    /// Spawns a door and marks it powered: there is no APC in the disconnected pool, and
    /// the bolt API refuses to act on an unpowered door.
    /// </summary>
    private EntityUid SpawnPoweredDoor(EntProtoId proto, EntityCoordinates coords)
    {
        var door = SSpawnAtPosition(proto, coords);
        SComp<ApcPowerReceiverComponent>(door).Powered = true;
        return door;
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
