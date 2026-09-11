using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Machine litanies. P4.3: ActivateDoor toggles the bolt on the holy NeoTheology door the
/// caster faces and refuses ordinary station doors. P4.10: MakeCruciform starts the adjacent
/// forge's run, RepairDoor heals the adjacent damaged door for the caster's biomatter,
/// PowerBiogenerator toggles the adjacent generator, and the two bioreactor litanies drive the
/// adjacent reactor's chamber.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsMachinesTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId HolyDoorProto = "OxydNtHolyDoor";
    private static readonly EntProtoId PlainDoorProto = "Airlock";
    private static readonly EntProtoId ForgeProto = "OxydNtCruciformForge";
    private static readonly EntProtoId BiogeneratorProto = "OxydNtBiogenerator";
    private static readonly EntProtoId BioreactorProto = "OxydNtBioreactor";
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";
    private static readonly EntProtoId PlasteelProto = "SheetPlasteel";
    private static readonly EntProtoId GoldProto = "IngotGold";
    private static readonly ProtoId<LitanyPrototype> ActivateDoor = "OxydLitanyActivateDoor";
    private static readonly ProtoId<LitanyPrototype> MakeCruciform = "OxydLitanyMakeCruciform";
    private static readonly ProtoId<LitanyPrototype> RepairDoor = "OxydLitanyRepairDoor";
    private static readonly ProtoId<LitanyPrototype> PowerBiogenerator = "OxydLitanyPowerBiogenerator";
    private static readonly ProtoId<LitanyPrototype> BioreactorSolution = "OxydLitanyBioreactorSolution";
    private static readonly ProtoId<LitanyPrototype> BioreactorChamber = "OxydLitanyBioreactorChamber";

    /// <summary>Eris <c>REPAIR_DOOR_AMOUNT</c>: the biomatter a repair burns.</summary>
    private const int RepairCost = 10;

    private const int PileSize = 25;

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;
    [SidedDependency(Side.Server)] private readonly MaterialStorageSystem _materialStorage = default!;
    [SidedDependency(Side.Server)] private readonly SharedStackSystem _stack = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;

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

    [Test]
    public async Task MakeCruciform_StartsTheAdjacentForgeRun()
    {
        var map = await Pair.CreateTestMap();
        EntityUid forge = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            forge = SpawnForge(origin.Offset(new Vector2(1f, 0f)));
            Bank(forge, origin, BiomatterProto, 10);
            Bank(forge, origin, PlasteelProto, 5);
            Bank(forge, origin, GoldProto, 2);
            Assert.That(SComp<CruciformForgeComponent>(forge).Working, Is.False, "Setup: the forge must be idle.");

            var begin = _litany.TryBeginLitany(caster, MakeCruciform, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "MakeCruciform begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SComp<CruciformForgeComponent>(forge).Working, Is.True,
                    "The litany must start the forge's own run.");
                Assert.That(_materialStorage.GetMaterialAmount(forge, "Biomatter"), Is.EqualTo(0),
                    "The started run must debit the recipe.");
            });
        });
    }

    [Test]
    public async Task RepairDoor_HealsTheAdjacentDamagedDoor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid door = default;
        EntityUid pile = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            door = SpawnPoweredDoor(HolyDoorProto, origin.Offset(new Vector2(1f, 0f)));
            _damageable.TryChangeDamage(door,
                new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>("Blunt"), 5), true);
            pile = SSpawnAtPosition(BiomatterProto, origin);
            _stack.SetCount((Entity<StackComponent?>) pile, PileSize);

            Assert.That(DamageOf(door), Is.Not.EqualTo(FixedPoint2.Zero), "Setup: the door must be damaged.");

            var begin = _litany.TryBeginLitany(caster, RepairDoor, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "RepairDoor begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(DamageOf(door), Is.EqualTo(FixedPoint2.Zero),
                    "The litany must fully heal the holy door.");
                Assert.That(SComp<StackComponent>(pile).Count, Is.EqualTo(PileSize - RepairCost),
                    "The ritual must eat exactly RepairCost biomatter.");
            });
        });
    }

    [Test]
    public async Task PowerBiogenerator_TogglesTheAdjacentMachine()
    {
        var map = await Pair.CreateTestMap();
        EntityUid generator = default;
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            generator = SSpawnAtPosition(BiogeneratorProto, origin.Offset(new Vector2(1f, 0f)));
            Assert.That(SComp<BiogeneratorComponent>(generator).Working, Is.False,
                "Setup: the biogenerator must start switched off.");

            var begin = _litany.TryBeginLitany(caster, PowerBiogenerator, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "PowerBiogenerator begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BiogeneratorComponent>(generator).Working, Is.True,
                "The first cast must switch the biogenerator on.");
        });

        // Second cast toggles it back off.
        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var begin = _litany.TryBeginLitany(caster, PowerBiogenerator, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "PowerBiogenerator second begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BiogeneratorComponent>(generator).Working, Is.False,
                "The second cast must switch the biogenerator off again.");
        });
    }

    [Test]
    public async Task BioreactorSolution_PumpsTheAdjacentChamber()
    {
        var map = await Pair.CreateTestMap();
        EntityUid reactor = default;
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            reactor = SSpawnAtPosition(BioreactorProto, origin.Offset(new Vector2(1f, 0f)));
            Assert.That(SComp<BioreactorComponent>(reactor).ChamberSolution, Is.False,
                "Setup: the sealed chamber must start dry.");

            var begin = _litany.TryBeginLitany(caster, BioreactorSolution, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "BioreactorSolution begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BioreactorComponent>(reactor).ChamberSolution, Is.True,
                "The first cast must pump solution into the sealed chamber.");
        });

        // Second cast pumps the chamber back out.
        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var begin = _litany.TryBeginLitany(caster, BioreactorSolution, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "BioreactorSolution second begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BioreactorComponent>(reactor).ChamberSolution, Is.False,
                "The second cast must pump the chamber back out.");
        });
    }

    [Test]
    public async Task BioreactorChamber_TogglesTheAdjacentChamberDoor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid reactor = default;

        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingClearActors();

            var origin = TileCentre(map.GridCoords);
            var caster = SpawnBearer(origin);
            _litany.TestingTreatAsActor(caster);

            reactor = SSpawnAtPosition(BioreactorProto, origin.Offset(new Vector2(1f, 0f)));
            Assert.That(SComp<BioreactorComponent>(reactor).ChamberClosed, Is.True,
                "Setup: the chamber must start sealed.");

            var begin = _litany.TryBeginLitany(caster, BioreactorChamber, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "BioreactorChamber begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BioreactorComponent>(reactor).ChamberClosed, Is.False,
                "The litany must open the unbreached, unsolved chamber door.");
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

    /// <summary>
    /// Spawns the real forge prototype and marks it powered: MaterialStorageSystem refuses
    /// insertion into an unpowered ApcPowerReceiver, and there is no APC in the disconnected pool.
    /// </summary>
    private EntityUid SpawnForge(EntityCoordinates coords)
    {
        var forge = SSpawnAtPosition(ForgeProto, coords);
        SComp<ApcPowerReceiverComponent>(forge).Powered = true;
        return forge;
    }

    /// <summary>Banks a stack in the forge through the real MaterialStorage insertion path.</summary>
    private void Bank(EntityUid forge, EntityCoordinates coords, EntProtoId proto, int count)
    {
        var items = SSpawnAtPosition(proto, coords);
        _stack.SetCount((Entity<StackComponent?>) items, count);

        Assert.That(_materialStorage.TryInsertMaterialEntity(forge, items, forge), Is.True,
            $"Setup: banks {count} {proto} in the forge.");
    }

    /// <summary>Total positive damage on an entity, via the non-obsolete damage API.</summary>
    private FixedPoint2 DamageOf(EntityUid uid) =>
        _damageable.GetPositiveDamage((uid, SComp<DamageableComponent>(uid))).GetTotal();

    /// <summary>Waits out the cast DoAfter / extra delay until no pending cast remains.</summary>
    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 240; i++)
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
