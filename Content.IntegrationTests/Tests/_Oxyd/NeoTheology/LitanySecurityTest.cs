using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 3 security fragments: stale UI, cancel, NPC forged speech.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanySecurityTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId BibleProto = "OxydNtBible";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task ForgedActorOnSpeechEvent_DoesNotCastForNpc()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingSetAvailabilityOverride(Relief.Id, true);
            var other = SSpawnAtPosition(HumanProto, map.GridCoords);
            var implant = _implants.AddImplant(other, CruciformProto);
            Assert.That(implant, Is.Not.Null);
            Assert.That(_cruciform.Activate(other), Is.True);
            var phrase = _prototypes.Index(Relief).Phrase;
            var beforeOther = _cruciform.GetHoliness(other);

            _litany.TestingHandleSpeech(new LitanySpeechAcceptedEvent(
                other, phrase, phrase, LitanySpeechKind.Speak, 99,
                InGameICChatType.Speak, false));

            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(other), Is.EqualTo(beforeOther));
        });
    }

    [Test]
    public async Task CancelClearsPendingWithoutDebit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var before = _cruciform.GetHoliness(body);
            var begin = _litany.TryBeginLitany(body, Relief, LitanyCastOrigin.ManualSpeech);
            Assert.That(begin.Success, Is.True);
            Assert.That(begin.RequestId, Is.Not.Null);

            var cancel = _litany.TryCancelLitany(body, begin.RequestId!);
            Assert.That(cancel.Success, Is.False);
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task ClosedBookBeginWithoutHeldBook_Fails()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            var before = _cruciform.GetHoliness(body);
            var revision = SComp<CruciformBearerComponent>(body).UiRevision;
            var result = _litany.TryBeginLitany(
                body, Relief, LitanyCastOrigin.Book, book: book, expectedRevision: revision);
            Assert.That(result.Success, Is.False);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(before));
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
}
