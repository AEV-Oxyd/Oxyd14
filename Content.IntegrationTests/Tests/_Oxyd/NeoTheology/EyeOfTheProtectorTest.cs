using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Shared.StatusEffectNew;
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
    private static readonly EntProtoId BlessingProto = "OxydNtEyeBlessing";

    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly EyeOfTheProtectorSystem _eye = default!;
    [SidedDependency(Side.Server)] private readonly ObeliskSystem _obelisk = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly StatusEffectsSystem _statusEffects = default!;

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

    [Test]
    public async Task ObeliskTickFeedsTheEyeObservation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            var obelisk = SSpawnAtPosition(null, map.GridCoords);
            SEntMan.AddComponent<ObeliskComponent>(obelisk);
            ActiveBearer(map.GridCoords);

            _obelisk.Tick(obelisk);

            Assert.That(eyeComp.Observation, Is.EqualTo(eyeComp.ObservationPerFaithful).Within(1e-6),
                "An obelisk pulse over one faithful must feed the Eye one ObservationPerFaithful.");
        });
    }

    [Test]
    public async Task ActiveBearerInRangeGainsBlessing()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var body = ActiveBearer(map.GridCoords);

            _eye.Scan(eye);

            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.True,
                "An active faithful in range must be blessed each scan.");
        });
    }

    [Test]
    public async Task BearerOutsideRadiusGainsNoBlessing()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            var body = ActiveBearer(map.GridCoords.Offset(new Vector2(eyeComp.ObservationRadius * 3f, 0f)));

            _eye.Scan(eye);

            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.False,
                "A faithful outside the radius must not be blessed.");
        });
    }

    [Test]
    public async Task BlessingLapsesAfterDuration()
    {
        var map = await Pair.CreateTestMap();
        EntityUid eye = default;
        EntityUid body = default;
        var duration = TimeSpan.Zero;

        await Server.WaitAssertion(() =>
        {
            eye = SpawnEye(map.GridCoords);
            duration = SComp<EyeOfTheProtectorComponent>(eye).FaithfulBlessingDuration;
            body = ActiveBearer(map.GridCoords);

            _eye.Scan(eye);

            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.True,
                "Setup: the scan must bless the faithful before the lapse check.");

            // Stop the eye from re-scanning so the blessing is not refreshed — the same thing that
            // happens when a bearer walks out of the radius and the scan stops reaching them.
            SEntMan.RemoveComponent<EyeOfTheProtectorComponent>(eye);
        });

        await Pair.RunSeconds((float)duration.TotalSeconds + 1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.False,
                "The blessing must lapse once its duration elapses without a refresh.");
        });
    }

    private EntityUid SpawnEye(EntityCoordinates coords)
    {
        // ponytail: the prototype arrives in P3.7; until then the scan is exercised on a bare entity.
        var eye = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<EyeOfTheProtectorComponent>(eye);
        return eye;
    }

    [Test]
    public async Task ScanAccruesArmamentPointsFromObservation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            eyeComp.Observation = 250f;

            _eye.Scan(eye);

            Assert.That(eyeComp.ArmamentsPoints, Is.EqualTo(2),
                "A scan must accrue (int)(Observation / 100) armament points.");
        });
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
