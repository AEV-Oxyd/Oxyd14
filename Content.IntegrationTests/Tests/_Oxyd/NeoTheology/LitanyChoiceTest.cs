using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Atmos.Components;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Stage 3 choice protocol: a book cast of Sending, Scrying or Confirmation pauses in the
/// Choosing stage, publishes server-authored options, and applies only the token the caster
/// selected. Manual-speech casts keep their deterministic fallback and are covered by the
/// per-litany suites.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyChoiceTest : SocialNoticeGameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId BibleProto = "OxydNtBible";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Inquisitor = "OxydNtInquisitor";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Custodian = "OxydNtCustodian";
    private static readonly ProtoId<LitanyPrototype> Scrying = "OxydLitanyScrying";
    private static readonly ProtoId<LitanyPrototype> Sending = "OxydLitanySending";
    private static readonly ProtoId<LitanyPrototype> Confirmation = "OxydLitanyConfirmation";

    private const string SendingText = "The eye watches the halls.";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly LitanyEffectSystem _effects = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = default!;

    [Test]
    public async Task Scrying_BookChoiceNarrowsToTheSelectedFollower()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid book = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid chosen = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Inquisitor);
            first = SpawnBearer(origin.Offset(new Vector2(2f, 0f)), Disciple);
            second = SpawnBearer(origin.Offset(new Vector2(-2f, 0f)), Disciple);
            book = HoldBook(caster, origin);

            var begin = BeginFromBook(caster, book, Scrying);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Scrying begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.Stage, Is.EqualTo(LitanyCastStage.Choosing));
            Assert.That(cast.ChoiceTargets, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(cast.ChoiceTargets, Does.Contain(first));
            Assert.That(cast.ChoiceTargets, Does.Contain(second));
            Assert.That(cast.ChoiceExpiresAt, Is.GreaterThan(_timing.CurTime));

            // Pick a candidate that is not the deterministic first target.
            chosen = cast.ChoiceTargets.First(target => target != first);
            Assert.That(chosen, Is.Not.EqualTo(first));
            var selection = Submit(caster, begin.RequestId!, [$"t:{cast.ChoiceTargets.Count - 1}"]);
            Assert.That(selection.Success, Is.True, selection.Reason?.Id ?? "Scrying choice failed");
            Assert.That(cast.AwaitingChoice, Is.False);
            Assert.That(cast.Targets, Is.EqualTo(new List<EntityUid> { chosen }));
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(STryComp<ScryingSessionComponent>(caster, out var session), Is.True,
                "A completed Scrying cast must start a session.");
            var markerCoords = SComp<TransformComponent>(session!.Marker!.Value).Coordinates;
            Assert.That(markerCoords, Is.EqualTo(SComp<TransformComponent>(chosen).Coordinates),
                "The session marker must follow the follower the caster selected.");
            Assert.That(markerCoords, Is.Not.EqualTo(SComp<TransformComponent>(first).Coordinates),
                "The marker must not sit on the deterministic first candidate.");
        });
    }

    [Test]
    public async Task Sending_BookChoiceDeliversTextOnlyToTheSelectedFollower()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid receiver = default;
        EntityUid other = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Inquisitor);
            other = SpawnBearer(origin.Offset(new Vector2(2f, 0f)), Disciple);
            receiver = SpawnBearer(origin.Offset(new Vector2(-2f, 0f)), Disciple);
            var book = HoldBook(caster, origin);
            _effects.TestingClearSocialNotices();

            var begin = BeginFromBook(caster, book, Sending);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Sending begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.ChoiceAllowsPlainText, Is.True);
            Assert.That(cast.ChoiceTargets, Has.Count.GreaterThanOrEqualTo(2));

            var index = cast.ChoiceTargets.IndexOf(receiver);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The receiver must be a candidate.");
            var selection = Submit(caster, begin.RequestId!, [$"t:{index}"], SendingText);
            Assert.That(selection.Success, Is.True, selection.Reason?.Id ?? "Sending choice failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            var received = _effects.TestingGetSocialNotices(receiver);
            Assert.That(received, Has.Count.EqualTo(1),
                "The selected follower must receive exactly one message.");
            Assert.That(received[0], Does.Contain(SendingText));
            Assert.That(_effects.TestingGetSocialNotices(other), Is.Empty,
                "No other follower may receive the private message.");
        });
    }

    [Test]
    public async Task Confirmation_BookChoiceAppliesTheSelectedDesignation()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;
        EntityUid target = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Preacher);
            target = SpawnBearer(origin.Offset(new Vector2(1f, 0f)), Disciple);
            var book = HoldBook(caster, origin);

            var begin = BeginFromBook(caster, book, Confirmation);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Confirmation begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var cast), Is.True);
            Assert.That(cast!.ChoiceDesignations, Does.Contain(Custodian));

            var selection = Submit(caster, begin.RequestId!, [$"d:{Custodian.Id}"]);
            Assert.That(selection.Success, Is.True, selection.Reason?.Id ?? "Confirmation choice failed");
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_cruciform.TryGetCruciform(target, out _, out var component), Is.True);
            Assert.That(component.Profile, Is.EqualTo(Custodian),
                "Confirmation must apply the designation the caster selected, not the Acolyte default.");
        });
    }

    [Test]
    public async Task Choice_InvalidTokenFailsClosedWithoutDebit()
    {
        var map = await Pair.CreateTestMap();
        EntityUid caster = default;

        await Server.WaitAssertion(() =>
        {
            var origin = TileCentre(map.GridCoords);
            caster = PrepareCaster(origin, Inquisitor);
            SpawnBearer(origin.Offset(new Vector2(2f, 0f)), Disciple);
            SpawnBearer(origin.Offset(new Vector2(-2f, 0f)), Disciple);
            var book = HoldBook(caster, origin);

            var begin = BeginFromBook(caster, book, Scrying);
            Assert.That(begin.Success, Is.True, begin.Reason?.Id ?? "Scrying begin failed");
            Assert.That(_litany.TestingTryGetPending(begin.RequestId!, out var pending), Is.True);
            Assert.That(pending!.AwaitingChoice, Is.True, "Two candidates must offer a target choice.");

            var implant = SComp<CruciformComponent>(SComp<CruciformBearerComponent>(caster).Cruciform!.Value);
            var holiness = implant.Holiness;
            var selection = Submit(caster, begin.RequestId!, ["t:99"]);
            Assert.That(selection.Success, Is.False);
            Assert.That(selection.Reason?.Id, Is.EqualTo("oxyd-litany-choice-invalid"));
            Assert.That(_litany.TestingPendingCount, Is.Zero);
            Assert.That(implant.Holiness, Is.GreaterThanOrEqualTo(holiness),
                "A rejected choice must not debit the cost.");
        });
    }

    /// <summary>Centre of the tile at <paramref name="gridCoords"/> so tile math is unambiguous.</summary>
    private static EntityCoordinates TileCentre(EntityCoordinates gridCoords)
        => gridCoords.Offset(new Vector2(0.5f, 0.5f));

    private EntityUid HoldBook(EntityUid body, EntityCoordinates coords)
    {
        var book = SSpawnAtPosition(BibleProto, coords);
        Assert.That(_hands.TryPickup(body, book), Is.True);
        Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);
        return book;
    }

    private LitanyActionResult BeginFromBook(EntityUid actor, EntityUid book, ProtoId<LitanyPrototype> litany)
    {
        var revision = SComp<CruciformBearerComponent>(actor).UiRevision;
        return _litany.TestingHandleBeginMessage(book, actor,
            new BeginLitanyMessage(litany, revision));
    }

    private LitanyActionResult Submit(EntityUid actor, string requestId, List<string> tokens, string? text = null)
        => _litany.TestingSubmitChoices(actor, requestId, tokens, text);

    /// <summary>A living active caster with the test actor flag and a full cruciform.</summary>
    private EntityUid PrepareCaster(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype> rank)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingClearActors();
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, rank);
        component.Holiness = component.MaxHoliness;
        StabilizeNeeds(body);
        return body;
    }

    /// <summary>A living active bearer at the given rank — the choice candidate.</summary>
    private EntityUid SpawnBearer(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype> rank)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);

        var component = SComp<CruciformComponent>(implant!.Value);
        _cruciform.MakeRank(implant.Value, component, rank);
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
