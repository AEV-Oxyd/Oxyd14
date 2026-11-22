using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.5: the holy-door repair ritual burns biomatter lying on the tiles the caster can
/// reach to fully heal the door.
/// </summary>
[TestOf(typeof(NeoTheologyDoorSystem))]
public sealed class NeoTheologyDoorTest : GameTest
{
    private static readonly EntProtoId DoorProto = "OxydNtHolyDoor";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";

    private const int RepairCost = 10;
    private const int PileSize = 25;

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly NeoTheologyDoorSystem _doors = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;

    [Test]
    public async Task RepairHealsDoorAndConsumesBiomatter()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var door = SpawnDoor(map.GridCoords, damaged: true);
            var user = SSpawnAtPosition(HumanProto, map.GridCoords);
            var pile = SpawnPile(map.GridCoords.Offset(new Vector2(1f, 0f)), PileSize);

            Assert.That(_doors.TryRepair(door, user, RepairCost), Is.True, Describe(user, pile, door));
            Assert.That(DamageOf(door), Is.EqualTo(FixedPoint2.Zero),
                "The repaired door must be back to full health.");
            Assert.That(SComp<StackComponent>(pile).Count, Is.EqualTo(PileSize - RepairCost),
                "The ritual must eat exactly RepairCost biomatter.");
        });
    }

    [Test]
    public async Task RepairWithoutBiomatterFailsAndLeavesDamage()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var door = SpawnDoor(map.GridCoords, damaged: true);
            var user = SSpawnAtPosition(HumanProto, map.GridCoords);

            Assert.That(_doors.TryRepair(door, user, RepairCost), Is.False,
                "A repair with no biomatter in reach must fail.");
            Assert.That(DamageOf(door), Is.Not.EqualTo(FixedPoint2.Zero),
                "A failed repair must leave the door damaged.");
        });
    }

    [Test]
    public async Task RepairOnUndamagedDoorFailsAndKeepsBiomatter()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var door = SpawnDoor(map.GridCoords, damaged: false);
            var user = SSpawnAtPosition(HumanProto, map.GridCoords);
            var pile = SpawnPile(map.GridCoords.Offset(new Vector2(1f, 0f)), PileSize);

            Assert.That(_doors.TryRepair(door, user, RepairCost), Is.False,
                "A door at full health has nothing to repair.");
            Assert.That(SComp<StackComponent>(pile).Count, Is.EqualTo(PileSize),
                "A refused repair must not eat the biomatter.");
        });
    }

    /// <summary>Total positive damage on an entity, via the non-obsolete damage API.</summary>
    private FixedPoint2 DamageOf(EntityUid uid) =>
        _damageable.GetPositiveDamage((uid, SComp<DamageableComponent>(uid))).GetTotal();

    /// <summary>Failure-message dump of everything the repair scan depends on.</summary>
    private string Describe(EntityUid user, EntityUid pile, EntityUid door)
    {
        var lookup = SEntMan.System<EntityLookupSystem>();
        var xform = SEntMan.GetComponent<TransformComponent>(user);
        var front = xform.Coordinates.Offset(xform.WorldRotation.ToVec());
        var stack = SComp<StackComponent>(pile);

        return $"user={xform.Coordinates} front={front} rot={xform.WorldRotation} " +
               $"pile={SEntMan.GetComponent<TransformComponent>(pile).Coordinates} " +
               $"count={stack.Count} type={stack.StackTypeId} " +
               $"nearUser={lookup.GetEntitiesInRange<StackComponent>(xform.Coordinates, 1f).Count} " +
               $"nearFront={lookup.GetEntitiesInRange<StackComponent>(front, 1f).Count} " +
               $"damage={DamageOf(door)}";
    }

    /// <summary>Spawns a holy door two tiles in front of the caster, optionally damaged.</summary>
    private EntityUid SpawnDoor(EntityCoordinates coords, bool damaged)
    {
        var door = SSpawnAtPosition(DoorProto, coords.Offset(new Vector2(2f, 0f)));

        if (damaged)
            _damageable.TryChangeDamage(door,
                new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>("Blunt"), 5), true);

        return door;
    }

    private EntityUid SpawnPile(EntityCoordinates coords, int count)
    {
        var pile = SSpawnAtPosition(BiomatterProto, coords);
        _stack.SetCount((Entity<StackComponent?>) pile, count);
        return pile;
    }
}
