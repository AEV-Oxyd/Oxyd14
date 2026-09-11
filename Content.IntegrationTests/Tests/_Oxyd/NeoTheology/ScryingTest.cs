using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P3.9: a scrying session retargets the caster's eye onto a marker at the target, restores it and
/// deletes the marker once the duration lapses, and refuses a second concurrent session.
/// </summary>
[TestOf(typeof(ScryingSystem))]
public sealed class ScryingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [SidedDependency(Side.Server)] private readonly ScryingSystem _scrying = default!;

    [Test]
    public async Task StartSessionRetargetsEyeOntoMarker()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = SpawnCaster(map.GridCoords);
            var target = SSpawnAtPosition(null, map.GridCoords);

            Assert.That(_scrying.TryStartSession(caster, target, TimeSpan.FromSeconds(10)), Is.True,
                "A caster with an eye must be able to start a session.");

            var marker = SComp<EyeComponent>(caster).Target;
            Assert.That(marker, Is.Not.Null, "The caster's eye must be retargeted onto the marker.");
            Assert.That(marker, Is.Not.EqualTo(caster), "The marker must be a distinct entity.");
            Assert.That(marker, Is.Not.EqualTo(target), "The marker must not be the target itself.");
            Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.True,
                "The caster must carry the session component.");
        });
    }

    [Test]
    public async Task SessionRestoresEyeAndDeletesMarkerAfterDuration()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid marker = default;
        var duration = TimeSpan.FromSeconds(2);

        await Server.WaitAssertion(() =>
        {
            caster = SpawnCaster(map.GridCoords);
            var target = SSpawnAtPosition(null, map.GridCoords);

            Assert.That(_scrying.TryStartSession(caster, target, duration), Is.True);
            marker = SComp<EyeComponent>(caster).Target!.Value;
        });

        await Pair.RunSeconds((float)duration.TotalSeconds + 1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<EyeComponent>(caster).Target, Is.Null,
                "The eye must be restored once the session lapses.");
            Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.False,
                "The session component must be removed once the session lapses.");
            Assert.That(SEntMan.Deleted(marker), Is.True,
                "The marker must be deleted once the session lapses.");
        });
    }

    [Test]
    public async Task SecondSessionWhileActiveIsRefused()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = SpawnCaster(map.GridCoords);
            var targetA = SSpawnAtPosition(null, map.GridCoords);
            var targetB = SSpawnAtPosition(null, map.GridCoords);

            Assert.That(_scrying.TryStartSession(caster, targetA, TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(_scrying.TryStartSession(caster, targetB, TimeSpan.FromSeconds(10)), Is.False,
                "A second session must be refused while one is already active.");
        });
    }

    [Test]
    public async Task DeathRestoresPreviousTargetAndDeletesMarker()
    {
        var map = await Pair.CreateTestMap();
        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            var caster = SpawnCaster(map.GridCoords);
            SEntMan.AddComponent<MobStateComponent>(caster);
            var previous = SSpawnAtPosition(null, map.GridCoords);
            SEntMan.System<SharedEyeSystem>().SetTarget(caster, previous);
            Assert.That(_scrying.TryStartSession(caster, previous, TimeSpan.FromSeconds(30)), Is.True);
            marker = SComp<ScryingSessionComponent>(caster).Marker!.Value;
            SEntMan.System<MobStateSystem>().ChangeMobState(caster, MobState.Dead);
            Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.False);
            Assert.That(SComp<EyeComponent>(caster).Target, Is.EqualTo(previous));
        });
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(marker), Is.True));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LosingControlEndsTheSession(bool disconnect)
    {
        var map = await Pair.CreateTestMap();
        var player = await Server.AddDummySession();
        EntityUid caster = default;
        EntityUid marker = default;
        await Server.WaitAssertion(() =>
        {
            caster = SpawnCaster(map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(player, caster);
            var target = SSpawnAtPosition(null, map.GridCoords);
            Assert.That(_scrying.TryStartSession(caster, target, TimeSpan.FromSeconds(30)), Is.True);
            marker = SComp<ScryingSessionComponent>(caster).Marker!.Value;
        });
        if (disconnect)
            await Server.RemoveDummySession(player);
        else
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(player, null));
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.False);
            Assert.That(SComp<EyeComponent>(caster).Target, Is.Null);
            Assert.That(SEntMan.Deleted(marker), Is.True);
        });
    }

    private EntityUid SpawnCaster(EntityCoordinates coords)
    {
        var caster = SSpawnAtPosition(null, coords);
        SEntMan.AddComponent<EyeComponent>(caster);
        return caster;
    }
}
