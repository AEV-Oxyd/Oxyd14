using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Server.Chat.Systems;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Radio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 3: speech recognition and cast transaction (nonce, cost, cooldown, DoAfter).
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyCastTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId BibleProto = "OxydNtBible";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";
    // Still dependency-gated (disabled) — used as the "recognized but rejected" fixture.
    private static readonly ProtoId<LitanyPrototype> Rejection = "OxydLitanyRejection";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly ChatSystem _chat = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task ManualSpeech_CastSucceedsOnceAndDebitsOnce()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        double holinessBefore = 0;

        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords);
            holinessBefore = _cruciform.GetHoliness(body);
            var phrase = _prototypes.Index(Relief).Phrase;

            _litany.TestingHandleSpeech(new EntitySpokeEvent(
                body, phrase, null, null, phrase));

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Not.Null);
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessBefore - 20).Within(0.01));
            Assert.That(SComp<CruciformBearerComponent>(body).PersonalCooldowns.ContainsKey(Relief.Id), Is.True);
        });
    }

    [Test]
    public async Task ChatSpeakPath_EmitsAcceptedEventAndBeginsCast()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var phrase = _prototypes.Index(Relief).Phrase;
            _chat.TrySendInGameICMessage(
                body,
                phrase,
                InGameICChatType.Speak,
                hideChat: false,
                checkRadioPrefix: false);
            // LitanySystem's subscription on the chat hook must begin the cast.
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1),
                "Accepted local Speak must begin a pending litany cast.");
        });
    }

    [Test]
    public async Task BookSpeech_DoesNotDualFireAndCommitsOnce()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        double holinessBefore = 0;

        await Server.WaitAssertion(() =>
        {
            body = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(body, book), Is.True);
            holinessBefore = _cruciform.GetHoliness(body);
            var revision = SComp<CruciformBearerComponent>(body).UiRevision;
            var result = _litany.TryBeginLitany(
                body, Relief, LitanyCastOrigin.Book, book: book, expectedRevision: revision);
            Assert.That(result.Success, Is.True, result.Reason?.Id ?? "begin failed");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
        });

        await AdvancePastCast();

        await Server.WaitAssertion(() =>
        {
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(holinessBefore - 20).Within(0.01));
        });
    }

    [Test]
    public async Task StutterException_MatchesOriginalMessageOnly()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var phrase = _prototypes.Index(Relief).Phrase;
            var garbled = "S-s-semper invicta.";

            _litany.TestingHandleSpeech(new EntitySpokeEvent(
                body, garbled, null, null, phrase));

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RadioSpeech_IsRejected()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var phrase = _prototypes.Index(Relief).Phrase;
            var before = _cruciform.GetHoliness(body);

            _litany.TestingHandleSpeech(new EntitySpokeEvent(
                body, phrase, _prototypes.Index<RadioChannelPrototype>("Common"), phrase, phrase));

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task NpcWithoutActor_IsRejected()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingSetAvailabilityOverride(Relief.Id, true);
            // Do NOT treat as actor.
            var npc = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = _implants.AddImplant(npc, CruciformProto);
            Assert.That(implant, Is.Not.Null);
            Assert.That(_cruciform.Activate(npc), Is.True);
            var phrase = _prototypes.Index(Relief).Phrase;
            var before = _cruciform.GetHoliness(npc);

            _litany.TestingHandleSpeech(new EntitySpokeEvent(npc, phrase, null, null, phrase));

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(npc), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task DuplicateAcceptedEvents_DoNotDoubleDebit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var before = _cruciform.GetHoliness(body);
            var first = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(first.Success, Is.True);
            var second = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(second.Success, Is.False);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task StaleUiRevision_IsRejectedWithoutDebit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(body, book), Is.True);
            var before = _cruciform.GetHoliness(body);
            var stale = SComp<CruciformBearerComponent>(body).UiRevision + 99;
            var result = _litany.TryBeginLitany(
                body, Relief, LitanyCastOrigin.Book, book: book, expectedRevision: stale);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason?.Id, Is.EqualTo("oxyd-litany-denied-stale-revision"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task FailedPrecondition_NoDebit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            Assert.That(_cruciform.TrySpend(body, _cruciform.GetHoliness(body)), Is.True);
            var before = _cruciform.GetHoliness(body);
            Assert.That(before, Is.LessThan(20));
            var result = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason?.Id, Is.EqualTo("oxyd-litany-no-cost"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task UnavailableWithoutOverride_RecognizedButRejected()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            _litany.TestingClearAvailabilityOverrides();
            var before = _cruciform.GetHoliness(body);
            Assert.That(_prototypes.Index(Rejection).IsAvailable, Is.False);
            var result = _litany.TryBeginLitany(body, Rejection, LitanyCastOrigin.ManualSpeech);
            Assert.That(result.Success, Is.False);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task ShippedCatalog_OnlyPacketCEffectsAvailable()
    {
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            var available = _prototypes.EnumeratePrototypes<LitanyPrototype>()
                .Where(l => l.IsAvailable)
                .Select(l => l.ID)
                .OrderBy(id => id)
                .ToArray();
            Assert.That(available, Is.EquivalentTo(new[]
            {
                "OxydLitanyCruciformSense",
                "OxydLitanyEntreaty",
                Relief.Id,
                "OxydLitanySoulHunger",
                "OxydLitanyActivateDoor",
                "OxydLitanyHandOfMercy",
                "OxydLitanyAbsolutionOfWounds",
                "OxydLitanyConvalescence",
                "OxydLitanySuccour",
                "OxydLitanyGraceOfPerseverance",
                "OxydLitanyUpholdHolyWord",
                "OxydLitanyRevelation",
                "OxydLitanyEpiphany",
                "OxydLitanyDivineBlessing",
                "OxydLitanyCommitment",
                "OxydLitanyDeprivation",
                "OxydLitanyConfirmation",
                "OxydLitanyAdoption",
                "OxydLitanyOrdination",
                "OxydLitanyOmission",
                "OxydLitanyExcommunication",
                "OxydLitanyInstallUpgrade",
                "OxydLitanyUninstallUpgrade",
                "OxydLitanyReincarnation",
                "OxydLitanyResurrection",
                "OxydLitanyMakeCruciform",
                "OxydLitanyRepairDoor",
                "OxydLitanyPowerBiogenerator",
                "OxydLitanyBioreactorSolution",
                "OxydLitanyBioreactorChamber",
                "OxydLitanyScrying",
                "OxydLitanyDivineIntervention",
                "OxydLitanyHolyGuidance",
                "OxydLitanyOrderArmaments",
                "OxydLitanyInitiation",
                "OxydLitanySending",
            }));
        });
    }

    [Test]
    public async Task DebitTolerance_UsesSelectedRules_NotHardcodedFallback()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            // Selected rules tolerance is 1e-6. 0.0005 below cost: the old 0.001
            // fallback allowed it, the selected rules must reject.
            var before = _cruciform.GetHoliness(body);
            Assert.That(_cruciform.TrySpend(body, before), Is.True);
            _cruciform.Refund(body, 20 - 0.0005);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(20 - 0.0005).Within(0.00001));

            var result = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason?.Id, Is.EqualTo("oxyd-litany-no-cost"));
        });
    }

    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingSetAvailabilityOverride(Relief.Id, true);
        var body = SSpawnAtPosition(HumanProto, coords);
        _litany.TestingTreatAsActor(body);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        return body;
    }

    private async Task AdvancePastCast()
    {
        for (var i = 0; i < 60; i++)
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
