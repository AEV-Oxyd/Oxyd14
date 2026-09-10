using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P3.2: the Eye of the Protector banks observation from active faithful in its radius. The Eye
/// entity prototype only lands in P3.7, so these tests build a bare entity and attach the
/// component themselves — the same trick <see cref="ObeliskTest.SpawnObelisk"/> uses.
/// </summary>
[TestOf(typeof(EyeOfTheProtectorSystem))]
public sealed class EyeOfTheProtectorTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly EyeOfTheProtectorSystem _eye = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    [Test]
    public async Task ActiveBearerInRadiusRaisesObservation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            ActiveBearer(map.GridCoords);

            _eye.Scan(eye);

            Assert.That(eyeComp.Observation, Is.EqualTo(eyeComp.ObservationPerFaithful).Within(1e-6),
                "One active faithful in range must bank one ObservationPerFaithful.");
        });
    }

    [Test]
    public async Task BearerOutsideRadiusDoesNotRaiseObservation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            ActiveBearer(map.GridCoords.Offset(new Vector2(eyeComp.ObservationRadius * 3f, 0f)));

            _eye.Scan(eye);

            Assert.That(eyeComp.Observation, Is.EqualTo(0f),
                "A faithful outside the observation radius must not bank observation.");
        });
    }

    [Test]
    public async Task SameBearerScannedTwiceAwardsOnce()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            ActiveBearer(map.GridCoords);

            _eye.Scan(eye);
            _eye.Scan(eye);

            Assert.That(eyeComp.Observation, Is.EqualTo(eyeComp.ObservationPerFaithful).Within(1e-6),
                "The same bearer scanned twice in one window must award only once.");
        });
    }

    private EntityUid SpawnEye(EntityCoordinates coords)
    {
        // ponytail: the prototype arrives in P3.7; until then the scan is exercised on a bare entity.
        var eye = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<EyeOfTheProtectorComponent>(eye);
        return eye;
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
