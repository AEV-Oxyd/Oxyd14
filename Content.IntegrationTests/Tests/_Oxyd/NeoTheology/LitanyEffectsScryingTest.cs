using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.13 Scrying: the inquisitor's litany starts the bounded P3.9 remote-view session on a
/// same-station follower and fails closed when there is nobody else to watch.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsScryingTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Inquisitor = "OxydNtInquisitor";
    private static readonly ProtoId<LitanyPrototype> Scrying = "OxydLitanyScrying";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task Scrying_StartsABoundedSessionOnAStationFollower()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            SpawnBearer(origin.Offset(new Vector2(2f, 0f)));

            var begin = _litany.TryBeginLitany(caster, Scrying, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Scrying begin failed");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(STryComp<ScryingSessionComponent>(caster, out var session), Is.True,
                "A completed Scrying cast must start a session on the caster.");
            Assert.That(SComp<EyeComponent>(caster).Target, Is.Not.Null,
                "The caster's eye must be retargeted onto the session marker.");

            // Eris bounds scrying to 300 ds; the YAML effectDuration carries the same 30 s.
            var remaining = session!.EndsAt - _timing.CurTime;
            Assert.That(remaining, Is.GreaterThan(TimeSpan.FromSeconds(25)),
                "The session must still be live right after the cast.");
            Assert.That(remaining, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(30)),
                "The session must not outlive the declared 30 s.");
        });
    }

    [Test]
    public async Task Scrying_WithNoOtherFollower_FailsClosed()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            caster = PrepareCaster(TileCentre(map.GridCoords));

            // StationFollower tolerates zero recipients at resolution; the effect refuses the
            // begin because there is nobody to scry.
            var begin = _litany.TryBeginLitany(caster, Scrying, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(SEntMan.HasComponent<ScryingSessionComponent>(caster), Is.False,
                "A refused begin must not leave a session behind.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>
    /// A living inquisitor with the test actor flag, an eye and a full cruciform: Scrying is
    /// inquisitor-set, so the caster must be entitled to it.
    /// </summary>
    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeInquisitor(implant.Value, component);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>A living active disciple — the scried body.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, Disciple);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>
    /// Removes the environment drift the other litany suites also strip: satiation decay damage and
    /// the vacuum test map's barotrauma so a ~1-2 s cast cannot kill the participants.
    /// </summary>
    private void StabilizeNeeds(EntityUid body)
    {
        if (SEntMan.TryGetComponent(body, out SatiationComponent satiation))
        {
            var sat = new Entity<SatiationComponent>(body, satiation);
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Hunger) is { } maxH)
                _satiation.SetValue(sat, SatiationSystem.Hunger, Math.Min(80f, (float) maxH));
            if (_satiation.GetMaximumValue(sat, SatiationSystem.Thirst) is { } maxT)
                _satiation.SetValue(sat, SatiationSystem.Thirst, (float) maxT);
        }

        SEntMan.RemoveComponent<SatiationDamageComponent>(body);
        SEntMan.RemoveComponent<BarotraumaComponent>(body);
    }

    /// <summary>Waits out the chant DoAfter until no pending cast remains.</summary>
    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 150; i++)
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
