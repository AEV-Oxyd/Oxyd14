using System.Numerics;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants;
using Content.Server.Power.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

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
    [SidedDependency(Side.Server)] private readonly IRobustRandom _random = default!;

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

    [Test]
    public async Task PowerAccruesWithoutSpendingObservation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            _random.SetSeed(42);
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            eyeComp.Observation = 500f;
            eyeComp.NextPowerUpdate = TimeSpan.Zero;

            _eye.UpdatePower(eye, eyeComp);

            Assert.That(eyeComp.NextPowerUpdate, Is.GreaterThan(TimeSpan.Zero),
                "The power update must reschedule itself.");
            Assert.That(eyeComp.NextMiracle, Is.EqualTo(eyeComp.NextPowerUpdate),
                "The UI cooldown must follow the power update.");
            Assert.That(eyeComp.Power, Is.EqualTo(7f).Within(1e-6),
                "Power gains 2 + observation/100 while below max.");
            Assert.That(eyeComp.Observation, Is.EqualTo(500f),
                "Power accrual must not spend the observation bank.");
            Assert.That(eyeComp.ArmamentsPoints, Is.Zero,
                "Armament points only arrive on a miracle release.");

            _eye.UpdatePower(eye, eyeComp);
            Assert.That(eyeComp.Power, Is.EqualTo(7f).Within(1e-6),
                "A second update inside the interval must not accrue again.");
        });
    }

    [Test]
    public async Task PowerReleaseBanksArmamentsAndCaps()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            _random.SetSeed(1337);
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            eyeComp.Observation = 1500f;
            eyeComp.Power = eyeComp.MaxPower - 1f;
            eyeComp.NextPowerUpdate = TimeSpan.Zero;

            _eye.UpdatePower(eye, eyeComp);

            Assert.That(eyeComp.ArmamentsPoints, Is.EqualTo(125),
                "A release banks the Eris armaments_rate.");
            Assert.That(eyeComp.Power, Is.EqualTo(16f).Within(1e-6),
                "Max power is spent on release; the remainder stays.");

            eyeComp.Power = eyeComp.MaxPower;
            eyeComp.NextPowerUpdate = TimeSpan.Zero;
            _eye.UpdatePower(eye, eyeComp);
            Assert.That(eyeComp.ArmamentsPoints, Is.EqualTo(150),
                "Armament points cap at the Eris maximum.");
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
    public async Task ScanDoesNotAccrueArmamentPoints()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var eyeComp = SComp<EyeOfTheProtectorComponent>(eye);
            eyeComp.Observation = 250f;

            _eye.Scan(eye);

            Assert.That(eyeComp.ArmamentsPoints, Is.Zero,
                "Armament points come from miracle releases, not scans.");
        });
    }

    [Test]
    public async Task EyeAndObelisksShareRecordsAcrossScanWindows()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var comp = SComp<EyeOfTheProtectorComponent>(eye);
            var first = SSpawnAtPosition(null, map.GridCoords);
            var second = SSpawnAtPosition(null, map.GridCoords);
            SEntMan.AddComponent<ObeliskComponent>(first);
            SEntMan.AddComponent<ObeliskComponent>(second);
            ActiveBearer(map.GridCoords);
            SSpawnAtPosition(HumanProto, map.GridCoords);
            for (var i = 0; i < 5; i++)
            {
                comp.NextScan = TimeSpan.Zero;
                _eye.Update(0);
                _obelisk.Tick(first);
                _obelisk.Tick(second);
            }
            Assert.That(comp.Observation, Is.EqualTo(30));
            Assert.That(comp.Scanned, Has.Count.EqualTo(2));

            // Move the observers away so a forgotten entry cannot be awarded again.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(eye,
                map.GridCoords.Offset(new Vector2(100f, 0f)));
            comp.NextRescan = TimeSpan.Zero;
            _eye.Scan(eye);
            Assert.That(comp.Scanned, Has.Count.EqualTo(1));
            Assert.That(comp.Observation, Is.EqualTo(comp.Scanned.Values.Single()));
            _eye.Scan(eye);
            Assert.That(comp.Scanned, Has.Count.EqualTo(1), "A second scan must not forget another body.");
            comp.NextRescan = TimeSpan.Zero;
            _eye.Scan(eye);
            Assert.That(comp.Scanned, Is.Empty);
            Assert.That(comp.Observation, Is.Zero);
        });
    }

    [Test]
    public async Task ObservationBoundsAndRecordedAwardsMatch()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = SpawnEye(map.GridCoords);
            var comp = SComp<EyeOfTheProtectorComponent>(eye);
            _eye.AddObservation(eye, -1000);
            Assert.That(comp.Observation, Is.EqualTo(-100));
            _eye.AddObservation(eye, 1895);
            var body = ActiveBearer(map.GridCoords);
            _eye.Scan(eye);
            Assert.That(comp.Observation, Is.EqualTo(1800));
            Assert.That(comp.Scanned[body], Is.EqualTo(5));
            SEntMan.DeleteEntity(body);
            comp.NextRescan = TimeSpan.Zero;
            _eye.Scan(eye);
            Assert.That(comp.Observation, Is.EqualTo(1795));
        });
    }

    [Test]
    public async Task UnpoweredEyeDoesNotScanBlessOrPayRewards()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var eye = SSpawnAtPosition("OxydNtEyeOfTheProtector", map.GridCoords);
            var comp = SComp<EyeOfTheProtectorComponent>(eye);
            var receiver = SComp<ApcPowerReceiverComponent>(eye);
            receiver.Powered = false;
            comp.Observation = 1500;
            var body = ActiveBearer(map.GridCoords);
            _eye.Scan(eye);
            _eye.Update(0);
            Assert.That(comp.Observation, Is.EqualTo(1500));
            Assert.That(comp.ArmamentsPoints, Is.Zero);
            Assert.That(comp.Scanned, Is.Empty);
            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.False);
            Assert.That(_eye.FindEye(body), Is.Null);

            Assert.That(SComp<TransformComponent>(eye).Anchored, Is.True);
            receiver.Powered = true;
            _eye.Scan(eye);
            Assert.That(comp.Observation, Is.EqualTo(1520));
            Assert.That(_statusEffects.HasStatusEffect(body, BlessingProto), Is.True);
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
