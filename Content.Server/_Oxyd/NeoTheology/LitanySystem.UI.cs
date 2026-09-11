using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Hands;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    /// <summary>
    /// Per-book open viewers. Private holiness/roles are never stored on the book
    /// component; this map only tracks which actors currently have the book UI open
    /// so close/drop can clear presentation and revoke pending book-local tokens.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _bookViewers = new();

    /// <summary>Test capture of the last actor-targeted snapshot per viewer.</summary>
    private readonly Dictionary<EntityUid, LitanyViewerSnapshotMessage> _testingLastSnapshot = new();

    private uint _publicCatalogRevision = 1;

    public int TestingSnapshotSendCount { get; private set; }

    public bool TestingTryGetLastSnapshot(EntityUid viewer, [NotNullWhen(true)] out LitanyViewerSnapshotMessage? snapshot)
    {
        return _testingLastSnapshot.TryGetValue(viewer, out snapshot);
    }

    public void TestingClearSnapshotCapture()
    {
        _testingLastSnapshot.Clear();
        TestingSnapshotSendCount = 0;
    }

    /// <summary>
    /// Builds the private viewer snapshot for one actor without requiring an open BUI.
    /// Integration tests use this to assert two-viewer privacy without a connected client.
    /// </summary>
    public LitanyViewerSnapshotMessage TestingBuildViewerSnapshot(EntityUid viewer)
    {
        return BuildViewerSnapshot(viewer);
    }

    /// <summary>
    /// Opens the book UI for <paramref name="actor"/> (server-side) and sends the
    /// private snapshot. Used by Packet A tests and stretch closed/drop coverage.
    /// </summary>
    public bool TestingOpenBookUi(EntityUid book, EntityUid actor)
    {
        if (!HasComp<LitanyBookComponent>(book))
            return false;

        _ui.OpenUi(book, LitanyUiKey.Book, actor);
        return _ui.GetActors(book, LitanyUiKey.Book).Contains(actor);
    }

    /// <summary>
    /// Simulates a begin message attributed to <paramref name="actor"/>. Rejects
    /// forged actors who do not have this book's UI open — no debit or pending cast.
    /// </summary>
    public LitanyActionResult TestingHandleBeginMessage(EntityUid book, EntityUid actor, BeginLitanyMessage message)
    {
        return HandleBeginLitany(book, actor, message);
    }

    private void InitializeUi()
    {
        SubscribeLocalEvent<LitanyBookComponent, BoundUIOpenedEvent>(OnBookUiOpened);
        SubscribeLocalEvent<LitanyBookComponent, BoundUIClosedEvent>(OnBookUiClosed);
        SubscribeLocalEvent<LitanyBookComponent, GotUnequippedHandEvent>(OnBookUnequipped);
        SubscribeLocalEvent<LitanyBookComponent, HandDeselectedEvent>(OnBookHandDeselected);
    }

    private void OnBookUiOpened(Entity<LitanyBookComponent> book, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not LitanyUiKey.Book)
            return;

        RememberViewer(book.Owner, args.Actor);

        // Public shared state only — never holiness/roles.
        _ui.SetUiState(book.Owner, LitanyUiKey.Book, new LitanyBookPublicState(_publicCatalogRevision));

        // Private holiness/roles/entries: actor-targeted only.
        SendViewerSnapshot(book.Owner, args.Actor);
    }

    private void OnBookUiClosed(Entity<LitanyBookComponent> book, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not LitanyUiKey.Book)
            return;

        ClearViewerState(book.Owner, args.Actor);
    }

    private void OnBookUnequipped(Entity<LitanyBookComponent> book, ref GotUnequippedHandEvent args)
    {
        CloseBookUiForActor(book.Owner, args.User);
    }

    private void OnBookHandDeselected(Entity<LitanyBookComponent> book, ref HandDeselectedEvent args)
    {
        // inHandsOnly + requireActiveHand: leaving the active hand closes the UI.
        CloseBookUiForActor(book.Owner, args.User);
    }

    private void CloseBookUiForActor(EntityUid book, EntityUid actor)
    {
        if (!_bookViewers.TryGetValue(book, out var viewers) || !viewers.Contains(actor))
        {
            // Still ask the UI system — single-user / drop may race the tracker.
            if (_ui.GetActors(book, LitanyUiKey.Book).Contains(actor))
                _ui.CloseUi(book, LitanyUiKey.Book, actor);
            return;
        }

        _ui.CloseUi(book, LitanyUiKey.Book, actor);
        ClearViewerState(book, actor);
    }

    private void OnBeginLitanyMessage(Entity<LitanyBookComponent> book, ref BeginLitanyMessage args)
    {
        // Actor is always the BUI session user set by SharedUserInterfaceSystem.
        // BeginLitanyMessage itself has no client-writable Actor field.
        HandleBeginLitany(book.Owner, args.Actor, args);
    }

    private LitanyActionResult HandleBeginLitany(EntityUid book, EntityUid actor, BeginLitanyMessage args)
    {
        // Forged / non-subscriber actors: reject with no state change.
        if (!_ui.GetActors(book, LitanyUiKey.Book).Contains(actor) &&
            !(_bookViewers.TryGetValue(book, out var viewers) && viewers.Contains(actor)))
        {
            return LitanyActionResult.Fail("oxyd-litany-denied-forged-actor");
        }

        // Stale revision refresh happens inside TryBeginLitany via
        // RefreshSnapshotOnStaleRevision — never authorize from the old list.
        return TryBeginLitany(
            actor,
            args.Litany,
            LitanyCastOrigin.Book,
            book: book,
            expectedRevision: args.StateRevision,
            choiceToken: args.ChoiceToken);
    }

    private void OnCancelLitanyMessage(Entity<LitanyBookComponent> book, ref CancelLitanyMessage args)
    {
        if (!_ui.GetActors(book.Owner, LitanyUiKey.Book).Contains(args.Actor))
            return;

        TryCancelLitany(args.Actor, args.RequestId);
    }

    /// <summary>
    /// Refreshes the actor-targeted snapshot when a book begin is rejected for a
    /// stale revision. Called from <see cref="TryBeginLitany"/> as a belt-and-suspenders
    /// path for direct API callers (cast tests).
    /// </summary>
    private void RefreshSnapshotOnStaleRevision(EntityUid actor, EntityUid? book)
    {
        if (book is not { } bookUid || !HasComp<LitanyBookComponent>(bookUid))
            return;

        if (_ui.GetActors(bookUid, LitanyUiKey.Book).Contains(actor) ||
            (_bookViewers.TryGetValue(bookUid, out var viewers) && viewers.Contains(actor)))
        {
            SendViewerSnapshot(bookUid, actor);
        }
        else
        {
            // Still capture for tests / future open; no broadcast of private data.
            var snapshot = BuildViewerSnapshot(actor);
            _testingLastSnapshot[actor] = snapshot;
            TestingSnapshotSendCount++;
        }
    }

    private void SendViewerSnapshot(EntityUid book, EntityUid actor)
    {
        var snapshot = BuildViewerSnapshot(actor);
        _testingLastSnapshot[actor] = snapshot;
        TestingSnapshotSendCount++;

        // Actor-targeted only — never SetUiState with holiness/roles.
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, snapshot, actor);
    }

    private EntityUid? FindActorBook(EntityUid actor)
    {
        foreach (var (book, viewers) in _bookViewers)
        {
            if (viewers.Contains(actor))
                return book;
        }

        return null;
    }

    private void SendResultToActor(EntityUid actor, LitanyActionResult result)
    {
        if (FindActorBook(actor) is not { } book)
            return;

        var revision = TryComp(actor, out CruciformBearerComponent? bearer) ? bearer.UiRevision : 0u;
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, new LitanyResultMessage(revision, result), actor);
    }

    private void SendProgressToActor(PendingLitanyCast cast)
    {
        if (FindActorBook(cast.Actor) is not { } book)
            return;

        var revision = TryComp(cast.Actor, out CruciformBearerComponent? bearer) ? bearer.UiRevision : 0u;
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, new LitanyProgressMessage(
            revision,
            cast.RequestId,
            cast.LitanyId,
            cast.Stage,
            cast.StartedAt,
            cast.ExpiresAt,
            canCancel: !cast.Committed), cast.Actor);
    }

    private void RefreshActorSnapshot(EntityUid actor)
    {
        if (FindActorBook(actor) is { } book)
            SendViewerSnapshot(book, actor);
    }

    private LitanyViewerSnapshotMessage BuildViewerSnapshot(EntityUid viewer)
    {
        var revision = 0u;
        var holiness = 0d;
        var cap = 0d;
        var regen = 0d;
        ProtoId<NeoTheologyProfilePrototype>? profile = null;
        var hasCruciform = false;
        var active = false;
        CruciformComponent? cruciformComp = null;
        LitanyBusyState? busy = null;

        if (_cruciform.TryGetCruciform(viewer, out _, out cruciformComp) &&
            TryComp(viewer, out CruciformBearerComponent? bearer))
        {
            hasCruciform = true;
            active = cruciformComp.Active;
            revision = bearer.UiRevision;
            holiness = _cruciform.GetHoliness(viewer);
            cap = _cruciform.GetMaximumHoliness(viewer);
            regen = _cruciform.GetRegenerationPerSecond(viewer);
            profile = cruciformComp.Profile;

            if (!string.IsNullOrEmpty(bearer.PendingRequestId) &&
                _pendingByRequest.TryGetValue(bearer.PendingRequestId, out var cast) &&
                cast.Actor == viewer)
            {
                busy = new LitanyBusyState(
                    cast.RequestId,
                    cast.LitanyId,
                    cast.Stage,
                    cast.StartedAt,
                    cast.ExpiresAt,
                    canCancel: !cast.Committed);
            }
        }

        var role = new LitanyRolePresentation(profile, hasCruciform, active);
        var entries = BuildViewerEntries(cruciformComp, active);

        return new LitanyViewerSnapshotMessage(
            revision,
            holiness,
            cap,
            regen,
            role,
            entries,
            busy);
    }

    private List<LitanyViewerEntry> BuildViewerEntries(CruciformComponent? cruciform, bool active)
    {
        var entries = new List<LitanyViewerEntry>();
        if (!_catalog.CatalogReady)
            return entries;

        foreach (var litany in _catalog.EnumerateCatalog()
                     .OrderBy(l => (int) l.Category)
                     .ThenBy(l => l.ID, StringComparer.Ordinal))
        {
            LocId? reason = null;
            var available = false;

            if (!IsEffectivelyAvailable(litany))
            {
                reason = litany.UnavailableReason ?? "oxyd-litany-unavailable-feature";
            }
            else if (cruciform is null || !active)
            {
                reason = cruciform is null
                    ? "oxyd-cruciform-no-implant"
                    : "oxyd-cruciform-inactive";
            }
            else if (!IsEntitled(cruciform, litany))
            {
                reason = "oxyd-litany-denied-entitlement";
            }
            else
            {
                available = true;
            }

            entries.Add(new LitanyViewerEntry(
                litany.ID,
                available,
                reason));
        }

        return entries;
    }

    private void RememberViewer(EntityUid book, EntityUid actor)
    {
        if (!_bookViewers.TryGetValue(book, out var viewers))
        {
            viewers = new HashSet<EntityUid>();
            _bookViewers[book] = viewers;
        }

        viewers.Add(actor);
    }

    private void ClearViewerState(EntityUid book, EntityUid actor)
    {
        if (_bookViewers.TryGetValue(book, out var viewers))
        {
            viewers.Remove(actor);
            if (viewers.Count == 0)
                _bookViewers.Remove(book);
        }

        _testingLastSnapshot.Remove(actor);

        // Revoke book-origin pending casts for this viewer when the UI is gone.
        if (TryComp(actor, out CruciformBearerComponent? bearer) &&
            !string.IsNullOrEmpty(bearer.PendingRequestId) &&
            _pendingByRequest.TryGetValue(bearer.PendingRequestId, out var cast) &&
            cast.Actor == actor &&
            cast.Origin == LitanyCastOrigin.Book &&
            cast.Book == book &&
            !cast.Committed)
        {
            if (cast.DoAfterId is { } doAfterId)
                _doAfter.Cancel(doAfterId);
            ClearPending(cast, cancelled: true);
        }
    }
}
