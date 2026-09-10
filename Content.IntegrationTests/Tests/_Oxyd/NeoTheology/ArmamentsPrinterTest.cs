using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P2.16: the armaments printer only sells to an active bearer of a configured profile, only within
/// reach, and only when the armory can pay — and it debits exactly what it charged.
/// </summary>
[TestOf(typeof(ArmamentsPrinterSystem))]
public sealed class ArmamentsPrinterTest : GameTest
{
    private static readonly EntProtoId PrinterProto = "OxydNtArmamentsPrinter";
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId ArmamentPath = "OxydNtBible";
    private const string ArmamentId = "OxydNtArmamentRitualBook";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly ArmamentsPrinterSystem _printer = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task APurchaseWithoutPointsSpawnsNothing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var printer = SSpawnAtPosition(PrinterProto, map.GridCoords);
            var buyer = ActiveBearer(map.GridCoords);

            Assert.That(SComp<ArmamentsPrinterComponent>(printer).Points, Is.Zero,
                "Setup: a fresh printer must start with no armament points.");

            Assert.That(_printer.TryPurchase(printer, buyer, ArmamentId), Is.False,
                "An empty armory must refuse the sale.");
            Assert.That(CountArmaments(), Is.Zero, "A refused sale must not spawn anything.");
        });
    }

    [Test]
    public async Task APurchaseWithPointsSpawnsOnThePrinterAndDebits()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var printer = SSpawnAtPosition(PrinterProto, map.GridCoords);
            var buyer = ActiveBearer(map.GridCoords);
            var component = SComp<ArmamentsPrinterComponent>(printer);
            var cost = _printer.GetCost(component, SProtoMan.Index<ArmamentPrototype>(ArmamentId));
            component.Points = cost + 10;

            Assert.That(_printer.TryPurchase(printer, buyer, ArmamentId), Is.True,
                "A stocked armory must sell to a faithful buyer in reach.");
            Assert.That(component.Points, Is.EqualTo(10), "The sale must debit exactly its price.");

            var spawned = Armaments().Single();
            Assert.That(SComp<TransformComponent>(spawned).Coordinates,
                Is.EqualTo(SComp<TransformComponent>(printer).Coordinates),
                "The armament must appear on the printer's turf.");
        });
    }

    private EntityUid ActiveBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null, "Setup: the cruciform must be implantable.");
        Assert.That(_cruciform.Activate(body), Is.True, "Setup: the bearer must be an active follower.");
        return body;
    }

    private List<EntityUid> Armaments()
    {
        var found = new List<EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == ArmamentPath.Id)
                found.Add(uid);
        }

        return found;
    }

    private int CountArmaments() => Armaments().Count;
}
