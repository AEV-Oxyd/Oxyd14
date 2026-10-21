using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    private uint _publicCatalogRevision = 1;

    private void RefreshOpenViewers()
    {
        var books = EntityQueryEnumerator<LitanyBookComponent>();
        while (books.MoveNext(out var book, out var bookComp))
        {
            foreach (var actor in bookComp.Viewers.ToArray())
            {
                if (TerminatingOrDeleted(actor))
                    continue;
                var signature = ComputeViewerSignature(actor);
                if (bookComp.LastSent.TryGetValue(actor, out var last) && last == signature)
                    continue;
                SendViewerSnapshot(book, actor);
            }
        }
    }

    private LitanyViewerSignature ComputeViewerSignature(EntityUid actor)
    {
        var revision = 0u;
        var holinessBucket = 0;
        var cooldownCount = 0;
        var cooldownNextEnd = 0;
        var busyStage = -1;
        string? busyRequestId = null;

        if (TryComp(actor, out CruciformBearerComponent? bearer))
        {
            revision = bearer.UiRevision;
            holinessBucket = (int) Math.Floor(_cruciform.GetHoliness(actor));

            var now = _timing.CurTime;
            var nextEnd = TimeSpan.MaxValue;
            foreach (var (_, expiry) in bearer.PersonalCooldowns)
            {
                if (expiry <= now)
                    continue;
                cooldownCount++;
                if (expiry < nextEnd)
                    nextEnd = expiry;
            }
            if (nextEnd != TimeSpan.MaxValue)
                cooldownNextEnd = (int) Math.Ceiling((nextEnd - now).TotalSeconds);
        }

        if (TryComp(actor, out LitanyPendingCastComponent? pending))
        {
            busyStage = (int) pending.Cast.Stage;
            busyRequestId = pending.Cast.RequestId;
        }

        return new LitanyViewerSignature(
            revision,
            holinessBucket,
            cooldownCount,
            cooldownNextEnd,
            busyStage,
            busyRequestId);
    }

    internal int TestingSnapshotSendCount { get; private set; }

    internal bool TestingTryGetLastSnapshot(EntityUid viewer, [NotNullWhen(true)] out LitanyViewerSnapshotMessage? snapshot)
    {
        if (TryComp(viewer, out LitanyTestingSnapshotComponent? capture) && capture.Last is { } last)
        {
            snapshot = last;
            return true;
        }

        snapshot = null;
        return false;
    }

    internal void TestingClearSnapshotCapture()
    {
        var query = EntityQueryEnumerator<LitanyTestingSnapshotComponent>();
        while (query.MoveNext(out var uid, out _))
            RemCompDeferred<LitanyTestingSnapshotComponent>(uid);
        TestingSnapshotSendCount = 0;
    }

    /// <summary>
    /// Builds the private viewer snapshot for one actor without requiring an open BUI.
    /// Integration tests use this to assert two-viewer privacy without a connected client.
    /// </summary>
    internal LitanyViewerSnapshotMessage TestingBuildViewerSnapshot(EntityUid viewer)
    {
        return BuildViewerSnapshot(viewer);
    }

    /// <summary>
    /// Opens the book UI for <paramref name="actor"/> (server-side) and sends the
    /// private snapshot. Used by Packet A tests and stretch closed/drop coverage.
    /// </summary>
    internal bool TestingOpenBookUi(EntityUid book, EntityUid actor)
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
    internal LitanyActionResult TestingHandleBeginMessage(EntityUid book, EntityUid actor, BeginLitanyMessage message)
    {
        return HandleBeginLitany(book, actor, message);
    }

    /// <summary>Designation labels and message text live on the server; the client only forwards tokens.</summary>
    [Dependency] private IPrototypeManager _prototypes = default!;

    [SubscribeLocalEvent]
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

    [SubscribeLocalEvent]
    private void OnBookUiClosed(Entity<LitanyBookComponent> book, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not LitanyUiKey.Book)
            return;

        ClearViewerState(book.Owner, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnBookUnequipped(Entity<LitanyBookComponent> book, ref GotUnequippedHandEvent args)
    {
        CloseBookUiForActor(book.Owner, args.User);
    }

    [SubscribeLocalEvent]
    private void OnBookHandDeselected(Entity<LitanyBookComponent> book, ref HandDeselectedEvent args)
    {
        // inHandsOnly + requireActiveHand: leaving the active hand closes the UI.
        CloseBookUiForActor(book.Owner, args.User);
    }

    private void CloseBookUiForActor(EntityUid book, EntityUid actor)
    {
        if (!TryComp(book, out LitanyBookComponent? bookComp) || !bookComp.Viewers.Contains(actor))
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
            !(TryComp(book, out LitanyBookComponent? bookComp) && bookComp.Viewers.Contains(actor)))
        {
            return LitanyActionResult.Fail("oxyd-litany-denied-forged-actor");
        }

        if (!_catalog.TryGetLitany(args.Litany, out var litany))
            return LitanyActionResult.Fail("oxyd-litany-denied-unknown");

        // Choice, blueprint, and named-target rites still open the prompt. Every other
        // rite is spoken: the book only chants the phrase, and speech casts it.
        var needsPrompt = litany.SelectTarget || litany.SelectBlueprint || litany.AllowPlainText ||
                          litany.DesignationChoices.Count > 0 ||
                          LitanyPhraseParser.HasTargetPlaceholder(litany.Phrase);
        if (needsPrompt)
        {
            return TryBeginLitany(
                actor,
                args.Litany,
                LitanyCastOrigin.Book,
                book: book,
                expectedRevision: args.StateRevision,
                choiceToken: args.ChoiceToken);
        }

        if (!_hands.IsHolding(actor, book) ||
            !_hands.TryGetActiveItem(actor, out var active) ||
            active != book)
            return LitanyActionResult.Fail("oxyd-litany-denied-book-hand");

        var delay = TimeSpan.FromSeconds(Math.Max(0.4, litany.Phrase.Length * 0.025));
        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            actor,
            delay,
            new LitanyBookSpeakDoAfterEvent(litany.ID),
            eventTarget: actor,
            target: null,
            used: book)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            RequireCanInteract = true,
            CancelDuplicate = true,
            BlockDuplicate = true,
        });

        return started
            ? LitanyActionResult.Ok()
            : LitanyActionResult.Fail("oxyd-litany-denied-doafter");
    }

    [SubscribeLocalEvent]
    private void OnBookSpeak(LitanyBookSpeakDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!_catalog.TryGetLitany(args.LitanyId, out var litany))
            return;

        _chat.TrySendInGameICMessage(
            args.User,
            litany.Phrase,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: false,
            shell: null,
            player: null,
            nameOverride: null,
            checkRadioPrefix: false,
            ignoreActionBlocker: false);
        args.Handled = true;
    }

    private void OnCancelLitanyMessage(Entity<LitanyBookComponent> book, ref CancelLitanyMessage args)
    {
        if (!_ui.GetActors(book.Owner, LitanyUiKey.Book).Contains(args.Actor))
            return;

        TryCancelLitany(args.Actor, args.RequestId);
    }

    [SubscribeLocalEvent]
    private void OnSubmitLitanyChoicesMessage(Entity<LitanyBookComponent> book, ref SubmitLitanyChoicesMessage args)
    {
        if (!_ui.GetActors(book.Owner, LitanyUiKey.Book).Contains(args.Actor))
            return;

        var result = SubmitChoicesCore(args.Actor, args.RequestId, args.SelectedTokens, args.RecipeId,
            args.PlainText, book.Owner);
        SendResultToActor(args.Actor, result, isFinal: !result.Success);
    }

    private void OnPromptSubmitChoices(Entity<CruciformComponent> prompt, ref SubmitLitanyChoicesMessage args)
    {
        if (!_ui.GetActors(prompt.Owner, LitanyUiKey.Book).Contains(args.Actor))
            return;

        var result = SubmitChoicesCore(args.Actor, args.RequestId, args.SelectedTokens, args.RecipeId,
            args.PlainText, prompt.Owner);
        SendResultToActor(args.Actor, result, isFinal: !result.Success);
    }

    private void OnPromptCancel(Entity<CruciformComponent> prompt, ref CancelLitanyMessage args)
    {
        if (!_ui.GetActors(prompt.Owner, LitanyUiKey.Book).Contains(args.Actor))
            return;

        TryCancelLitany(args.Actor, args.RequestId);
    }

    /// <summary>Test entry that mirrors the BUI choice submission without a connected client.</summary>
    internal LitanyActionResult TestingSubmitChoices(EntityUid actor, string requestId, List<string> tokens,
        string? plainText = null)
    {
        var result = SubmitChoicesCore(actor, requestId, tokens, recipeId: null, plainText, expectBook: null);
        SendResultToActor(actor, result, isFinal: !result.Success);
        return result;
    }

    /// <summary>
    /// Sends the actor the options for a Choosing cast. Target tokens are positional
    /// (<c>t:index</c>) so no entity/identity value leaves the server; designation tokens
    /// name the profile prototype the effect will apply.
    /// </summary>
    private void OpenPrayerPrompt(PendingLitanyCast cast)
    {
        // The spoken-prayer choice UI lives on the caster's cruciform entity — a real
        // entity inside the actor — rather than a nullspace proxy.
        cast.Prompt = cast.Cruciform;
        _ui.SetUi(cast.Cruciform, LitanyUiKey.Book, new InterfaceData("LitanyBoundUserInterface", 0f, false));
        _ui.OpenUi(cast.Cruciform, LitanyUiKey.Book, cast.Actor);
    }

    private void SendChoiceSnapshot(PendingLitanyCast cast)
    {
        // Speech-origin casts get a nullspace prayer prompt instead of a held book;
        // the snapshot must go to whichever BUI is actually open for the actor.
        var ui = cast.Prompt is { } prompt ? prompt : FindActorBook(cast.Actor);
        if (ui is not { } book)
            return;

        var options = new List<LitanyChoiceOption>();
        for (var i = 0; i < cast.ChoiceTargets.Count; i++)
            options.Add(new LitanyChoiceOption($"t:{i}", VisibleName(cast.Actor, cast.ChoiceTargets[i])));

        foreach (var profile in cast.ChoiceDesignations)
        {
            var label = _prototypes.TryIndex(profile, out var proto) ? Loc.GetString(proto.Name) : profile.Id;
            options.Add(new LitanyChoiceOption($"d:{profile.Id}", label));
        }

        foreach (var blueprint in cast.ChoiceBlueprints)
        {
            var label = _prototypes.TryIndex(blueprint, out NeoTheologyBlueprintPrototype? proto)
                ? Loc.GetString(proto.Name)
                : blueprint.Id;
            options.Add(new LitanyChoiceOption($"b:{blueprint.Id}", label));
        }

        var revision = TryComp(cast.Actor, out CruciformBearerComponent? bearer) ? bearer.UiRevision : 0u;
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, new LitanyChoiceSnapshotMessage(
            revision, cast.RequestId, cast.ChoiceExpiresAt, options), cast.Actor);
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
            (TryComp(bookUid, out LitanyBookComponent? bookComp) && bookComp.Viewers.Contains(actor)))
        {
            SendViewerSnapshot(bookUid, actor);
        }
        else
        {
            // Still capture for tests / future open; no broadcast of private data.
            var snapshot = BuildViewerSnapshot(actor);
            EnsureComp<LitanyTestingSnapshotComponent>(actor).Last = snapshot;
            TestingSnapshotSendCount++;
        }
    }

    private void SendViewerSnapshot(EntityUid book, EntityUid actor)
    {
        if (TryComp(book, out LitanyBookComponent? bookComp))
            bookComp.LastSent[actor] = ComputeViewerSignature(actor);

        var snapshot = BuildViewerSnapshot(actor);
        EnsureComp<LitanyTestingSnapshotComponent>(actor).Last = snapshot;
        TestingSnapshotSendCount++;

        // Actor-targeted only — never SetUiState with holiness/roles.
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, snapshot, actor);

        // A prompt opened on the open tick may not exist client-side yet, so
        // choice/progress sends issued then are dropped silently. Re-send them alongside
        // the (repeating) viewer snapshot — by the time one arrives, the prompt exists
        // client-side. Idempotent on the client.
        if (TryComp(actor, out LitanyPendingCastComponent? pending))
        {
            var cast = pending.Cast;
            SendProgressToActor(cast);
            if (cast.Stage == LitanyCastStage.Choosing &&
                (cast.ChoiceTargets.Count > 0 ||
                 cast.ChoiceDesignations.Count > 0 ||
                 cast.ChoiceBlueprints.Count > 0))
            {
                SendChoiceSnapshot(cast);
            }
        }
    }

    private EntityUid? FindActorBook(EntityUid actor)
    {
        if (TryComp<CruciformBearerComponent>(actor, out var bearer) &&
            bearer.PendingRequestId is { } request &&
            TryComp(actor, out LitanyPendingCastComponent? pending) &&
            pending.Cast.RequestId == request &&
            pending.Cast.Prompt is { } prompt)
            return prompt;

        var books = EntityQueryEnumerator<LitanyBookComponent>();
        while (books.MoveNext(out var book, out var bookComp))
        {
            if (bookComp.Viewers.Contains(actor))
                return book;
        }

        return null;
    }

    private void SendResultToActor(EntityUid actor, LitanyActionResult result, bool isFinal = true)
    {
        if (FindActorBook(actor) is not { } book)
            return;

        var revision = TryComp(actor, out CruciformBearerComponent? bearer) ? bearer.UiRevision : 0u;
        _ui.ServerSendUiMessage(book, LitanyUiKey.Book, new LitanyResultMessage(revision, result, isFinal), actor);
    }

    private void SendProgressToActor(PendingLitanyCast cast)
    {
        var ui = cast.Prompt is { } prompt ? prompt : FindActorBook(cast.Actor);
        if (ui is not { } book)
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
            // The client extrapolates holiness from this rate — zero it when
            // the server wouldn't accrue (inactive implant or dead bearer).
            regen = active && !_mobState.IsDead(viewer)
                ? _cruciform.GetRegenerationPerSecond(viewer)
                : 0d;
            profile = cruciformComp.Profile;

            if (!string.IsNullOrEmpty(bearer.PendingRequestId) &&
                TryComp(viewer, out LitanyPendingCastComponent? pendingComp) &&
                pendingComp.Cast.RequestId == bearer.PendingRequestId)
            {
                var cast = pendingComp.Cast;
                busy = new LitanyBusyState(
                    cast.RequestId,
                    cast.LitanyId,
                    cast.Stage,
                    cast.StartedAt,
                    cast.ExpiresAt,
                    canCancel: !cast.Committed);
            }
        }

        var role = new LitanyRolePresentation(
            profile,
            hasCruciform,
            active,
            cruciformComp?.Clearance ?? NeoTheologyClearance.None);
        var entries = BuildViewerEntries(viewer, cruciformComp, active);

        return new LitanyViewerSnapshotMessage(
            revision,
            holiness,
            cap,
            regen,
            role,
            entries,
            busy,
            cruciformComp?.LastHolinessUpdate ?? default);
    }

    private List<LitanyViewerEntry> BuildViewerEntries(EntityUid viewer, CruciformComponent? cruciform, bool active)
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
            else if (litany.TargetMode == LitanyTargetMode.Ceremony &&
                HasComp<ActiveCeremonyComponent>(viewer))
            {
                // Leading a rite occupies the slot — surface it instead of
                // letting the catalogue claim the litany is usable.
                reason = "oxyd-litany-ceremony-busy";
            }
            else if (TryComp<CruciformBearerComponent>(viewer, out var bearer) &&
                !IsCooldownAvailable(viewer, bearer, litany, out var cooldown))
            {
                reason = cooldown.Reason;
            }
            else
            {
                available = true;
            }

            var endsAt = TimeSpan.Zero;
            if (litany.CooldownScope == LitanyCooldownScope.Global)
                endsAt = GlobalCooldowns.GetValueOrDefault(litany.CooldownKey);
            else if (litany.CooldownScope == LitanyCooldownScope.Personal &&
                TryComp<CruciformBearerComponent>(viewer, out var owner))
                endsAt = owner.PersonalCooldowns.GetValueOrDefault(litany.CooldownKey);

            entries.Add(new LitanyViewerEntry(
                litany.ID,
                available,
                reason,
                endsAt));
        }

        return entries;
    }

    private void RememberViewer(EntityUid book, EntityUid actor)
    {
        if (TryComp(book, out LitanyBookComponent? bookComp))
            bookComp.Viewers.Add(actor);
    }

    private void ClearViewerState(EntityUid book, EntityUid actor)
    {
        if (TryComp(book, out LitanyBookComponent? bookComp))
        {
            bookComp.Viewers.Remove(actor);
            bookComp.LastSent.Remove(actor);
        }

        RemComp<LitanyTestingSnapshotComponent>(actor);

        // Revoke book-origin pending casts for this viewer when the UI is gone.
        if (TryComp(actor, out CruciformBearerComponent? bearer) &&
            !string.IsNullOrEmpty(bearer.PendingRequestId) &&
            TryComp(actor, out LitanyPendingCastComponent? pending) &&
            pending.Cast.RequestId == bearer.PendingRequestId &&
            (pending.Cast.Book == book || pending.Cast.Prompt == book) &&
            !pending.Cast.Committed)
        {
            var cast = pending.Cast;
            if (cast.DoAfterId is { } doAfterId)
                _doAfter.Cancel(doAfterId);
            ClearPending(cast, cancelled: true);
        }
    }
}
