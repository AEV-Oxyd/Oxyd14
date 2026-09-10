using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.1: litany target-mode resolution. <c>TryResolveTargets</c> turns world state
/// into a stable candidate list at begin time; commit replays the recorded list
/// (see <see cref="LitanyCastTest"/> for the transaction contract).
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyTargetTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId DoorProto = "OxydNtHolyDoor";
    private static readonly EntProtoId MachineProto = "OxydNtBioreactor";
    private static readonly EntProtoId WallProto = "WallSolid";

    // One catalog litany per resolved mode. Resolution does not check availability,
    // so disabled catalog entries are fine here.
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";                     // Self
    private static readonly ProtoId<LitanyPrototype> Revelation = "OxydLitanyRevelation";             // AdjacentLiving, range 4
    private static readonly ProtoId<LitanyPrototype> Confirmation = "OxydLitanyConfirmation";         // AdjacentFollower, range 1.5
    private static readonly ProtoId<LitanyPrototype> CruciformSense = "OxydLitanyCruciformSense";     // VisibleFollower, range 7
    private static readonly ProtoId<LitanyPrototype> Entreaty = "OxydLitanyEntreaty";                 // StationFollower
    private static readonly ProtoId<LitanyPrototype> ActivateDoor = "OxydLitanyActivateDoor";         // FrontMachine, range 1.5
    private static readonly ProtoId<LitanyPrototype> RepairDoor = "OxydLitanyRepairDoor";             // NearbyMachine, range 1.5
    private static readonly ProtoId<LitanyPrototype> GraceOfPerseverance = "OxydLitanyGraceOfPerseverance"; // VisibleArea, range 7
    private static readonly ProtoId<LitanyPrototype> DivineGuidance = "OxydLitanyDivineGuidance";     // FrontTile (deferred to P4.13)
    private static readonly ProtoId<LitanyPrototype> Sanctify = "OxydLitanySanctify";                 // Ceremony (deferred to P4.11)

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task Self_ResolvesOnlyTheActor()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Relief), out var targets, out var reason),
                Is.True, reason?.Id ?? "Self must resolve");
            Assert.That(targets, Is.EqualTo(new[] { caster }),
                "Self resolves the actor and nothing else.");
        });
    }

    [Test]
    public async Task AdjacentLiving_ResolvesOwnAndFacedTilesOnly()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            var sameTile = SSpawnAtPosition(HumanProto, origin);
            var ahead = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));
            var behind = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(-1f, 0f)));
            var twoAhead = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(2f, 0f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Revelation), out var targets, out var reason),
                Is.True, reason?.Id ?? "AdjacentLiving must resolve");
            Assert.That(targets, Is.EquivalentTo(new[] { sameTile, ahead }),
                "AdjacentLiving covers the actor's own tile and the faced tile only.");
            Assert.That(targets, Does.Not.Contain(behind), "A mob behind the actor must not resolve.");
            Assert.That(targets, Does.Not.Contain(twoAhead),
                "A mob two tiles ahead is beyond the faced tile even when inside range.");
        });
    }

    [Test]
    public async Task AdjacentFollower_ResolvesFacedActiveBearerOnly()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            var bearerAhead = SpawnBearer(origin.Offset(new Vector2(1f, 0f)));
            var bearerBehind = SpawnBearer(origin.Offset(new Vector2(-1f, 0f)));
            var mundaneAhead = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0.25f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Confirmation), out var targets, out var reason),
                Is.True, reason?.Id ?? "AdjacentFollower must resolve");
            Assert.That(targets, Is.EqualTo(new[] { bearerAhead }),
                "Only the active bearer on the faced tile resolves.");
            Assert.That(targets, Does.Not.Contain(mundaneAhead), "A non-bearer on the faced tile must not resolve.");
            Assert.That(targets, Does.Not.Contain(bearerBehind), "A bearer behind the actor must not resolve.");
        });
    }

    [Test]
    public async Task VisibleFollower_ResolvesLineOfSightBearersOnly()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid visible = default;
        EntityUid occluded = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            visible = SpawnBearer(origin.Offset(new Vector2(0f, 2f)));
            SSpawnAtPosition(WallProto, origin.Offset(new Vector2(1f, 0f)));
            occluded = SpawnBearer(origin.Offset(new Vector2(2f, 0f)));
        });

        // Let the freshly spawned wall register before the line-of-sight check.
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(CruciformSense), out var targets, out var reason),
                Is.True, reason?.Id ?? "VisibleFollower must resolve");
            Assert.That(targets, Is.EqualTo(new[] { visible }),
                "A bearer behind a wall must not resolve even inside the cruciform sense range.");
            Assert.That(targets, Does.Not.Contain(occluded));
        });
    }

    [Test]
    public async Task StationFollower_ResolvesEveryBearerRegardlessOfRange()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            var nearBearer = SpawnBearer(origin.Offset(new Vector2(2f, 0f)));
            var distantBearer = SpawnBearer(origin.Offset(new Vector2(6f, 0f)));
            SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(1f, 0f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Entreaty), out var targets, out var reason),
                Is.True, reason?.Id ?? "StationFollower must resolve");
            Assert.That(targets, Is.EquivalentTo(new[] { nearBearer, distantBearer }),
                "StationFollower resolves every other active bearer, whatever the distance.");
            Assert.That(targets, Does.Not.Contain(caster), "The actor is never their own target for follower modes.");

            // Determinism: the same world state must yield the same ordered list.
            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Entreaty), out var again, out _), Is.True);
            Assert.That(again, Is.EqualTo(targets));
        });
    }

    [Test]
    public async Task StationFollower_ToleratesZeroOtherBearers()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Entreaty), out var targets, out var reason),
                Is.True, "StationFollower is a broadcast mode: zero other bearers must still resolve.");
            Assert.That(targets, Is.Empty, "No other bearer exists, so the resolved list is empty.");
            Assert.That(reason, Is.Null, "An empty broadcast resolves cleanly, not as a failure.");
        });
    }

    [Test]
    public async Task Entreaty_CommitsWithZeroFollowers()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            var begin = _litany.TryBeginLitany(caster, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True,
                begin.Reason?.Id ?? "Entreaty must begin with no other followers on the station.");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0), "The cast must complete.");
            Assert.That(SComp<CruciformBearerComponent>(caster).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(caster), Is.EqualTo(50).Within(0.01),
                "Entreaty cost is 0 — an empty broadcast still commits without charge.");
        });
    }

    [Test]
    public async Task FrontMachine_ResolvesFacedTileMachineOnly()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            var ahead = SSpawnAtPosition(DoorProto, origin.Offset(new Vector2(1f, 0f)));
            SSpawnAtPosition(DoorProto, origin.Offset(new Vector2(-1f, 0f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(ActivateDoor), out var targets, out var reason),
                Is.True, reason?.Id ?? "FrontMachine must resolve");
            Assert.That(targets, Is.EqualTo(new[] { ahead }),
                "FrontMachine resolves the machine on the faced tile only.");
        });
    }

    [Test]
    public async Task NearbyMachine_ResolvesMachinesWithinRangeOnly()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            var near = SSpawnAtPosition(MachineProto, origin.Offset(new Vector2(1f, 0f)));
            SSpawnAtPosition(MachineProto, origin.Offset(new Vector2(2f, 0f)));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(RepairDoor), out var targets, out var reason),
                Is.True, reason?.Id ?? "NearbyMachine must resolve");
            Assert.That(targets, Is.EqualTo(new[] { near }),
                "Range 1.5: a machine two tiles away must not resolve.");
        });
    }

    [Test]
    public async Task VisibleArea_ResolvesVisibleMobsOnly()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid visible = default;
        EntityUid occluded = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            visible = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(0f, 2f)));
            SSpawnAtPosition(WallProto, origin.Offset(new Vector2(1f, 0f)));
            occluded = SSpawnAtPosition(HumanProto, origin.Offset(new Vector2(2f, 0f)));
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(GraceOfPerseverance), out var targets, out var reason),
                Is.True, reason?.Id ?? "VisibleArea must resolve");
            Assert.That(targets, Is.EqualTo(new[] { visible }),
                "VisibleArea resolves mobs with line of sight inside range only.");
            Assert.That(targets, Does.Not.Contain(caster));
            Assert.That(targets, Does.Not.Contain(occluded));
        });
    }

    [Test]
    public async Task BeginLitany_RecordsResolvedTargetsOnThePendingCast()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingSetAvailabilityOverride(Entreaty.Id, true);

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);
            var peer = SpawnBearer(origin.Offset(new Vector2(2f, 0f)));

            var begin = _litany.TryBeginLitany(caster, Entreaty, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "begin failed");
            Assert.That(begin.RequestId, Is.Not.Null);

            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.Targets, Is.EqualTo(new[] { peer }),
                "The begin transaction must record the resolved targets for commit.");
        });
    }

    [Test]
    public async Task DeferredModes_FailClosedWithNoTarget()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = SpawnBearer(TileCentre(map.GridCoords));

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(DivineGuidance), out var targets, out var reason),
                Is.False, "FrontTile resolution is deferred to P4.13 and must fail closed.");
            Assert.That(reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(targets, Is.Empty);

            Assert.That(_litany.TryResolveTargets(caster, _prototypes.Index(Sanctify), out targets, out reason),
                Is.False, "Ceremony resolution belongs to P4.11 and must fail closed.");
            Assert.That(reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(targets, Is.Empty);
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

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

    /// <summary>A human with an active cruciform: the shared actor/follower fixture.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        return body;
    }
}
