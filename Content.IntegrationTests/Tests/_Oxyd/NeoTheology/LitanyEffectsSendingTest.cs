using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// P4.13 Sending: the inquisitor's anonymous telepathic notice reaches every same-station follower
/// (Eris <c>rituals/inquisitor.dm:233-252</c>; the fork has no litany text input, so the prepared
/// anonymous notice is delivered to the whole station follower list) and fails closed with nobody
/// to receive it.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyEffectsSendingTest : SocialNoticeGameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Inquisitor = "OxydNtInquisitor";
    private static readonly ProtoId<LitanyPrototype> Sending = "OxydLitanySending";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;

    [Test]
    public async Task Sending_DeliversTheAnonymousNoticeToStationFollowers()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid recipient = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin);
            recipient = SpawnBearer(origin.Offset(new Vector2(2f, 0f)));
            _effects.TestingClearSocialNotices();

            var begin = _litany.TryBeginLitany(caster, Sending, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Sending begin failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_effects.TestingGetSocialNotices(recipient),
                Does.Contain(Loc.GetString("oxyd-litany-private-sending")),
                "The telepathic notice must reach the other station follower.");
            Assert.That(_effects.TestingGetSocialNotices(caster), Is.Empty,
                "Sending must not echo back to its own sender.");
        });
    }

    [Test]
    public async Task Sending_WithoutAnyFollower_FailsClosed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var caster = PrepareCaster(TileCentre(map.GridCoords));

            var begin = _litany.TryBeginLitany(caster, Sending, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.False);
            Assert.That(begin.Reason?.Id, Is.EqualTo("oxyd-litany-no-target"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    /// <summary>
    /// A living inquisitor with the test actor flag and a full cruciform: Sending is
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

    /// <summary>A living active disciple — the notices' recipient.</summary>
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
