using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Bible.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Prayer;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

/// <summary>
/// Milestone 4 Packet A: book UI contracts — private viewer snapshots, begin-message
/// shape, stale/forged request rejection, and OxydNtBible isolation from upstream Bible.
/// </summary>
[TestOf(typeof(LitanySystem))]
public sealed class LitanyUiTest : GameTest
{
    private static readonly EntProtoId CruciformProto = "OxydNtCruciform";
    private static readonly EntProtoId BibleProto = "OxydNtBible";
    private static readonly EntProtoId UpstreamBibleProto = "Bible";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly ProtoId<LitanyPrototype> Relief = "OxydLitanyRelief";
    private static readonly ProtoId<LitanyPrototype> SoulHunger = "OxydLitanySoulHunger";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Disciple = "OxydNtDisciple";
    private static readonly ProtoId<NeoTheologyProfilePrototype> Preacher = "OxydNtPreacher";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
    };

    [SidedDependency(Side.Server)] private readonly LitanySystem _litany = default!;
    [SidedDependency(Side.Server)] private readonly CruciformSystem _cruciform = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly SharedUserInterfaceSystem _ui = default!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _prototypes = default!;

    [Test]
    public async Task OxydNtBible_DoesNotInheritUpstreamBibleBehaviors()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Assert.That(_prototypes.TryIndex(BibleProto, out EntityPrototype oxydProto), Is.True);
            Assert.That(oxydProto!.Parents, Is.Not.Null.And.Contain("BaseItem"));
            Assert.That(oxydProto.Parents, Does.Not.Contain(UpstreamBibleProto.Id));

            foreach (var banned in new[]
                     {
                         "Bible", "Prayable", "Summonable", "ReactionMixer", "Storage",
                     })
            {
                Assert.That(oxydProto.Components.ContainsKey(banned), Is.False,
                    $"OxydNtBible must not inherit upstream component '{banned}'.");
            }

            Assert.That(oxydProto.Components.ContainsKey("LitanyBook"), Is.True);
            Assert.That(oxydProto.Components.ContainsKey("ActivatableUI"), Is.True);
            Assert.That(oxydProto.Components.ContainsKey("UserInterface"), Is.True);

            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(SEntMan.HasComponent<LitanyBookComponent>(book), Is.True);
            Assert.That(SEntMan.HasComponent<BibleComponent>(book), Is.False);
            Assert.That(SEntMan.HasComponent<PrayableComponent>(book), Is.False);
            Assert.That(SEntMan.HasComponent<SummonableComponent>(book), Is.False);
            Assert.That(SEntMan.HasComponent<ReactionMixerComponent>(book), Is.False);
            Assert.That(SEntMan.HasComponent<StorageComponent>(book), Is.False);

            var activatable = SComp<ActivatableUIComponent>(book);
            Assert.That(activatable.Key, Is.EqualTo(LitanyUiKey.Book));
            Assert.That(activatable.InHandsOnly, Is.True);
            Assert.That(activatable.RequireActiveHand, Is.True);
            Assert.That(activatable.RequiresComplex, Is.True);
            Assert.That(activatable.SingleUser, Is.True);
            Assert.That(activatable.BlockSpectators, Is.True);

            Assert.That(_ui.HasUi(book, LitanyUiKey.Book), Is.True);
            Assert.That(_ui.HasUi(book, StorageComponent.StorageUiKey.Key), Is.False,
                "OxydNtBible must not open upstream storage UI.");
        });
    }

    [Test]
    public async Task ViewerSnapshot_ShowsOwnHolinessRoles_NotPeer()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearSnapshotCapture();
            var viewerA = PrepareBearer(map.GridCoords, Disciple);
            var viewerB = PrepareBearer(map.GridCoords, Preacher);
            Assert.That(_cruciform.TrySpend(viewerA, 15d), Is.True);

            var holinessA = _cruciform.GetHoliness(viewerA);
            var holinessB = _cruciform.GetHoliness(viewerB);
            Assert.That(holinessA, Is.Not.EqualTo(holinessB).Within(0.01));

            Assert.That(_cruciform.TryGetCruciform(viewerA, out _, out var cruciformA), Is.True);
            Assert.That(_cruciform.TryGetCruciform(viewerB, out _, out var cruciformB), Is.True);
            Assert.That(cruciformA.Profile, Is.EqualTo(Disciple));
            Assert.That(cruciformB.Profile, Is.EqualTo(Preacher));

            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(SComp<LitanyBookComponent>(book).ReferenceCatalog, Is.True);

            var snapA = _litany.TestingBuildViewerSnapshot(viewerA);
            var snapB = _litany.TestingBuildViewerSnapshot(viewerB);

            Assert.That(snapA.Holiness, Is.EqualTo(holinessA).Within(0.01));
            Assert.That(snapB.Holiness, Is.EqualTo(holinessB).Within(0.01));
            Assert.That(snapA.Holiness, Is.Not.EqualTo(snapB.Holiness).Within(0.01));
            Assert.That(snapA.Cap, Is.EqualTo(_cruciform.GetMaximumHoliness(viewerA)).Within(0.01));
            Assert.That(snapB.Cap, Is.EqualTo(_cruciform.GetMaximumHoliness(viewerB)).Within(0.01));

            Assert.That(snapA.RolePresentation.Profile, Is.EqualTo(Disciple));
            Assert.That(snapB.RolePresentation.Profile, Is.EqualTo(Preacher));
            Assert.That(snapA.RolePresentation.Profile, Is.Not.EqualTo(snapB.RolePresentation.Profile));
            Assert.That(snapA.RolePresentation.HasCruciform, Is.True);
            Assert.That(snapB.RolePresentation.HasCruciform, Is.True);
            Assert.That(snapA.RolePresentation.Active, Is.True);
            Assert.That(snapB.RolePresentation.Active, Is.True);

            Assert.That(snapA.Revision, Is.EqualTo(SComp<CruciformBearerComponent>(viewerA).UiRevision));
            Assert.That(snapB.Revision, Is.EqualTo(SComp<CruciformBearerComponent>(viewerB).UiRevision));

            // Opening both against one physical book still yields actor-private snapshots.
            Assert.That(_hands.TryPickup(viewerA, book), Is.True);
            Assert.That(_litany.TestingOpenBookUi(book, viewerA), Is.True);
            Assert.That(_litany.TestingTryGetLastSnapshot(viewerA, out var sentA), Is.True);
            Assert.That(sentA!.Holiness, Is.EqualTo(holinessA).Within(0.01));
            Assert.That(sentA.RolePresentation.Profile, Is.EqualTo(Disciple));

            if (_ui.TryGetUiState<BoundUserInterfaceState>(book, LitanyUiKey.Book, out var shared))
            {
                Assert.That(shared, Is.TypeOf<LitanyBookPublicState>(),
                    "Shared SetUiState may only carry LitanyBookPublicState, never private holiness/roles.");
                Assert.That(shared, Is.Not.InstanceOf<LitanyViewerSnapshot>());
            }
        });
    }

    [Test]
    public async Task Entries_CarryViewerAvailabilityOnly()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            var catalog = _prototypes.EnumeratePrototypes<LitanyPrototype>().ToArray();
            Assert.That(catalog, Is.Not.Empty);

            var relief = _prototypes.Index(Relief);
            Assert.That(relief.Phrase, Is.EqualTo("Semper invicta."));
            Assert.That(relief.Cost, Is.EqualTo(20d).Within(1e-9));
            Assert.That(relief.Category, Is.EqualTo(LitanyCategory.Common));
            Assert.That(relief.UnavailableReason, Is.Null);
            Assert.That(relief.IsAvailable, Is.True);

            var viewer = PrepareBearer(map.GridCoords, Disciple);
            var viewerImplant = SComp<CruciformComponent>(
                SComp<CruciformBearerComponent>(viewer).Cruciform!.Value);
            var snapshot = _litany.TestingBuildViewerSnapshot(viewer);
            Assert.That(snapshot.Entries, Is.Not.Empty);
            Assert.That(snapshot.Entries.Count, Is.EqualTo(catalog.Length));

            var byId = snapshot.Entries.ToDictionary(e => e.Litany.Id);
            foreach (var litany in catalog)
            {
                Assert.That(byId.ContainsKey(litany.ID), Is.True, litany.ID);
                var entry = byId[litany.ID];
                // Prototype-set phrase/cost/category are read from the prototype client-side;
                // the BUI entry only carries viewer-specific availability.
                Assert.That(entry.Litany.Id, Is.EqualTo(litany.ID));
                // A disciple unlocks the Common + Machinery sets, so exactly the implemented
                // entries granted by those sets are available to this viewer.
                // Availability follows the enabled prototype data and the viewer's unlocked sets,
                // not a hardcoded checkpoint list.
                var expectAvailable = litany.IsAvailable
                    && litany.GrantedBy.Any(set => viewerImplant.UnlockedSets.Contains(set));
                Assert.That(entry.Available, Is.EqualTo(expectAvailable),
                    $"{litany.ID}: Common/Machinery handlers should be available for an entitled disciple.");
                if (!expectAvailable)
                    Assert.That(entry.UnavailableReason, Is.Not.Null, litany.ID);
            }

            var reliefEntry = byId[Relief.Id];
            Assert.That(reliefEntry.Available, Is.True);
            Assert.That(reliefEntry.UnavailableReason, Is.Null);
        });
    }

    [Test]
    public async Task BeginLitanyMessage_NoClientActorField_ActorFromBuiContext()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var declaredProps = typeof(BeginLitanyMessage)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(p => p.Name)
                .ToArray();
            Assert.That(declaredProps, Is.EquivalentTo(new[] { "Litany", "StateRevision", "ChoiceToken" }));
            Assert.That(declaredProps, Does.Not.Contain("Actor"));
            Assert.That(declaredProps, Does.Not.Contain("ClientActor"));
            Assert.That(typeof(BeginLitanyMessage).IsSubclassOf(typeof(BoundUserInterfaceMessage)), Is.True);

            var body = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(body, book), Is.True);
            Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);

            var revision = SComp<CruciformBearerComponent>(body).UiRevision;
            // Message carries no actor identity field — actor is supplied only via BUI context.
            var msg = new BeginLitanyMessage(Relief, revision);
            var result = _litany.TestingHandleBeginMessage(book, body, msg);

            Assert.That(result.Success, Is.True, result.Reason?.Id ?? "begin failed");
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(1));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Not.Null);

            _litany.TryCancelLitany(body, SComp<CruciformBearerComponent>(body).PendingRequestId!);
        });
    }

    [Test]
    public async Task StaleStateRevision_RefreshesSnapshot_DoesNotAuthorizeCast()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(body, book), Is.True);
            _litany.TestingClearSnapshotCapture();
            Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);

            var beforeHoliness = _cruciform.GetHoliness(body);
            var currentRevision = SComp<CruciformBearerComponent>(body).UiRevision;
            var staleRevision = currentRevision + 99u;
            var sendsBefore = _litany.TestingSnapshotSendCount;

            var msg = new BeginLitanyMessage(Relief, staleRevision);
            var result = _litany.TestingHandleBeginMessage(book, body, msg);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason?.Id, Is.EqualTo("oxyd-litany-denied-stale-revision"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(body).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(body), Is.EqualTo(beforeHoliness).Within(0.01));

            Assert.That(_litany.TestingSnapshotSendCount, Is.GreaterThan(sendsBefore),
                "Stale StateRevision must refresh/send a viewer snapshot.");
            Assert.That(_litany.TestingTryGetLastSnapshot(body, out var refreshed), Is.True);
            Assert.That(refreshed!.Revision, Is.EqualTo(SComp<CruciformBearerComponent>(body).UiRevision));
            Assert.That(refreshed.Revision, Is.Not.EqualTo(staleRevision));
            Assert.That(refreshed.Holiness, Is.EqualTo(beforeHoliness).Within(0.01));
        });
    }

    [Test]
    public async Task ForgedUiRequest_WrongActor_NoStateChange()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var holder = PrepareCaster(map.GridCoords);
            var peer = PrepareCaster(map.GridCoords);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(holder, book), Is.True);
            Assert.That(_litany.TestingOpenBookUi(book, holder), Is.True);

            var beforeHolder = _cruciform.GetHoliness(holder);
            var beforePeer = _cruciform.GetHoliness(peer);
            var revision = SComp<CruciformBearerComponent>(holder).UiRevision;
            var msg = new BeginLitanyMessage(Relief, revision);

            // Peer is not a subscriber of this book's UI — forged actor must be rejected.
            var forged = _litany.TestingHandleBeginMessage(book, peer, msg);
            Assert.That(forged.Success, Is.False);
            Assert.That(forged.Reason?.Id, Is.EqualTo("oxyd-litany-denied-forged-actor"));
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(SComp<CruciformBearerComponent>(holder).PendingRequestId, Is.Null);
            Assert.That(SComp<CruciformBearerComponent>(peer).PendingRequestId, Is.Null);
            Assert.That(_cruciform.GetHoliness(holder), Is.EqualTo(beforeHolder).Within(0.01));
            Assert.That(_cruciform.GetHoliness(peer), Is.EqualTo(beforePeer).Within(0.01));

            // Framework RaiseUiMessage with wrong Actor also must not deliver.
            _ui.RaiseUiMessage(book, LitanyUiKey.Book, new BeginLitanyMessage(Relief, revision) { Actor = peer });
            Assert.That(_litany.TestingPendingCount, Is.EqualTo(0));
            Assert.That(_cruciform.GetHoliness(holder), Is.EqualTo(beforeHolder).Within(0.01));
            Assert.That(_cruciform.GetHoliness(peer), Is.EqualTo(beforePeer).Within(0.01));
        });
    }

    [Test]
    public async Task Catalog_PacketCAvailable_WithHandlers()
    {
        await Server.WaitAssertion(() =>
        {
            _litany.TestingClearAvailabilityOverrides();
            var available = _prototypes.EnumeratePrototypes<LitanyPrototype>()
                .Where(l => l.IsAvailable)
                .Select(l => l.Effect)
                .ToHashSet();
            Assert.That(available, Is.EquivalentTo(new[]
            {
                LitanyEffectKind.Relief,
                LitanyEffectKind.SoulHunger,
                LitanyEffectKind.Entreaty,
                LitanyEffectKind.CruciformSense,
                LitanyEffectKind.ActivateDoor,
                LitanyEffectKind.HandOfMercy,
                LitanyEffectKind.AbsolutionOfWounds,
                LitanyEffectKind.Convalescence,
                LitanyEffectKind.Succour,
                LitanyEffectKind.GraceOfPerseverance,
                LitanyEffectKind.UpholdHolyWord,
                LitanyEffectKind.Revelation,
                LitanyEffectKind.Epiphany,
                LitanyEffectKind.DivineBlessing,
                LitanyEffectKind.Commitment,
                LitanyEffectKind.Deprivation,
                LitanyEffectKind.Confirmation,
                LitanyEffectKind.Adoption,
                LitanyEffectKind.Ordination,
                LitanyEffectKind.Omission,
                LitanyEffectKind.Excommunication,
                LitanyEffectKind.InstallUpgrade,
                LitanyEffectKind.UninstallUpgrade,
                LitanyEffectKind.Reincarnation,
                LitanyEffectKind.Resurrection,
                LitanyEffectKind.MakeCruciform,
                LitanyEffectKind.RepairDoor,
                LitanyEffectKind.PowerBiogenerator,
                LitanyEffectKind.BioreactorSolution,
                LitanyEffectKind.BioreactorChamber,
                LitanyEffectKind.Scrying,
                LitanyEffectKind.DivineIntervention,
                LitanyEffectKind.HolyGuidance,
                LitanyEffectKind.OrderArmaments,
                LitanyEffectKind.Initiation,
                LitanyEffectKind.Sending,
                LitanyEffectKind.BaptismalRecord,
                LitanyEffectKind.AcceleratedGrowth,
                LitanyEffectKind.Rejection,
                LitanyEffectKind.RevealAdversaries,
                LitanyEffectKind.WordsOfPurging,
                LitanyEffectKind.Atonement,
                LitanyEffectKind.Penance,
                LitanyEffectKind.Asacris,
                LitanyEffectKind.DivineGuidance,
                LitanyEffectKind.Manifestation,
                LitanyEffectKind.Uproot,
                LitanyEffectKind.Knowledge,
                LitanyEffectKind.Bounty,
                LitanyEffectKind.PoundingWhisper,
                LitanyEffectKind.RevelationOfSecrets,
                LitanyEffectKind.LispOfVitae,
                LitanyEffectKind.CantoOfCourage,
                LitanyEffectKind.ChantOfObservance,
                LitanyEffectKind.ReclamationOfEndurance,
                LitanyEffectKind.Sanctify,
                LitanyEffectKind.Crusade,
                LitanyEffectKind.EternalBrotherhood,
                LitanyEffectKind.CallToBattle,
                LitanyEffectKind.SearingRevelation,
            }));
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Relief), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.SoulHunger), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Entreaty), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.CruciformSense), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.ActivateDoor), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Revelation), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Epiphany), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.DivineBlessing), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Commitment), Is.True);
            Assert.That(LitanyHandlerCatalog.HasHandler(LitanyEffectKind.Deprivation), Is.True);
            Assert.That(LitanyHandlerCatalog.Implemented.Count, Is.EqualTo(60));
        });
    }

    [Test]
    public async Task BookUi_InHandsOnly_SingleUser()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var holder = PrepareBearer(map.GridCoords, Disciple);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);

            var activatable = SComp<ActivatableUIComponent>(book);
            Assert.That(activatable.InHandsOnly, Is.True);
            Assert.That(activatable.SingleUser, Is.True);
            Assert.That(activatable.RequireActiveHand, Is.True);

            _litany.TestingClearAvailabilityOverrides();
            _litany.TestingSetAvailabilityOverride(Relief.Id, true);
            _litany.TestingTreatAsActor(holder);
            var revision = SComp<CruciformBearerComponent>(holder).UiRevision;
            var unheld = _litany.TryBeginLitany(
                holder, Relief, LitanyCastOrigin.Book, book: book, expectedRevision: revision);
            Assert.That(unheld.Success, Is.False);

            Assert.That(_hands.TryPickup(holder, book), Is.True);
            Assert.That(_litany.TestingOpenBookUi(book, holder), Is.True);
            Assert.That(_ui.IsUiOpen(book, LitanyUiKey.Book, holder), Is.True);

            if (_ui.TryGetUiState<BoundUserInterfaceState>(book, LitanyUiKey.Book, out var shared))
            {
                Assert.That(shared, Is.TypeOf<LitanyBookPublicState>());
            }
        });
    }

    [Test]
    public async Task ClosedUi_BookDrop_ClearsViewerState()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = PrepareBearer(map.GridCoords, Disciple);
            var book = SSpawnAtPosition(BibleProto, map.GridCoords);
            Assert.That(_hands.TryPickup(body, book), Is.True);
            _litany.TestingClearSnapshotCapture();
            Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);
            Assert.That(_ui.IsUiOpen(book, LitanyUiKey.Book, body), Is.True);
            Assert.That(_litany.TestingTryGetLastSnapshot(body, out _), Is.True);

            _ui.CloseUi(book, LitanyUiKey.Book, body);
            Assert.That(_ui.IsUiOpen(book, LitanyUiKey.Book, body), Is.False);
            Assert.That(_litany.TestingTryGetLastSnapshot(body, out _), Is.False,
                "Closing the book UI must clear captured viewer snapshot state.");

            Assert.That(_litany.TestingOpenBookUi(book, body), Is.True);
            Assert.That(_hands.TryDrop(body, book, checkActionBlocker: false), Is.True);
            Assert.That(_ui.IsUiOpen(book, LitanyUiKey.Book, body), Is.False,
                "Dropping an in-hands-only litany book must close the viewer UI.");
            Assert.That(SComp<ActivatableUIComponent>(book).CurrentSingleUser, Is.Null);
            Assert.That(_litany.TestingTryGetLastSnapshot(body, out _), Is.False,
                "Book drop must clear viewer snapshot capture.");
        });
    }

    private EntityUid PrepareCaster(EntityCoordinates coords)
    {
        var body = PrepareBearer(coords, Disciple);
        _litany.TestingClearAvailabilityOverrides();
        _litany.TestingSetAvailabilityOverride(Relief.Id, true);
        _litany.TestingTreatAsActor(body);
        return body;
    }

    private EntityUid PrepareBearer(EntityCoordinates coords, ProtoId<NeoTheologyProfilePrototype> profile)
    {
        var body = SSpawnAtPosition(HumanProto, coords);
        var implant = _implants.AddImplant(body, CruciformProto);
        Assert.That(implant, Is.Not.Null);
        Assert.That(_cruciform.Activate(body), Is.True);
        if (!profile.Equals(Disciple))
            Assert.That(_cruciform.TrySetProfile(body, profile), Is.True);
        return body;
    }
}
