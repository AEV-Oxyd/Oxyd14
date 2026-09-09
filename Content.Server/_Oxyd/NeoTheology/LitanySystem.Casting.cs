using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    private bool StartCastDoAfter(PendingLitanyCast cast, TimeSpan delay, bool requireBook)
    {
        var args = new DoAfterArgs(
            EntityManager,
            cast.Actor,
            delay,
            new LitanyDoAfterEvent(cast.RequestId),
            eventTarget: cast.Actor,
            target: null,
            used: requireBook ? cast.Book : null)
        {
            BreakOnMove = true,
            MovementThreshold = 0.3f,
            BreakOnDamage = true,
            DamageThreshold = 1,
            NeedHand = requireBook,
            BreakOnDropItem = requireBook,
            BreakOnHandChange = requireBook,
            RequireCanInteract = true,
            CancelDuplicate = true,
            BlockDuplicate = true,
            Hidden = true,
            Broadcast = true,
        };

        if (!_doAfter.TryStartDoAfter(args, out var id))
            return false;

        cast.DoAfterId = id;
        return true;
    }

    private void OnLitanyDoAfter(LitanyDoAfterEvent args)
    {
        if (!_pendingByRequest.TryGetValue(args.RequestId, out var cast))
            return;

        if (args.Cancelled)
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (cast.Committed)
            return;

        if (cast.Actor != args.User)
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // Bearer pending cleared by lifecycle → abandon.
        if (!TryComp(cast.Actor, out CruciformBearerComponent? bearer) ||
            bearer.PendingRequestId != cast.RequestId)
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (!_actionBlocker.CanSpeak(cast.Actor))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        switch (cast.Stage)
        {
            case LitanyCastStage.Chanting when cast.Origin == LitanyCastOrigin.Book:
                EmitBookSpeechAndAwait(cast);
                break;
            case LitanyCastStage.Chanting when cast.Origin == LitanyCastOrigin.ManualSpeech:
                AfterChantComplete(cast);
                break;
            case LitanyCastStage.ExtraDelay:
                TryCommit(cast);
                break;
            default:
                ClearPending(cast, cancelled: true);
                break;
        }
    }

    private void EmitBookSpeechAndAwait(PendingLitanyCast cast)
    {
        if (cast.Book is not { } book ||
            !_hands.IsHolding(cast.Actor, book) ||
            !_hands.TryGetActiveItem(cast.Actor, out var active) ||
            active != book)
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // Claim the next accepted local Speak for this pending book cast.
        cast.AwaitingBookSpeech = true;
        cast.ExpectedSpeechSequence = _chat.PeekNextLitanySpeechSequence();

        // Speak without ignoreActionBlocker; radio prefixes disabled so the phrase is local.
        // TrySendInGameICMessage raises LitanySpeechAcceptedEvent synchronously on success.
        _chat.TrySendInGameICMessage(
            cast.Actor,
            cast.Phrase,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: false,
            shell: null,
            player: null,
            nameOverride: null,
            checkRadioPrefix: false,
            ignoreActionBlocker: false);

        // Speech may have matched inline (same stack). If still awaiting, no match/blocked → cancel.
        if (cast.AwaitingBookSpeech)
            ClearPending(cast, cancelled: true);
    }

    private void ContinueAfterBookSpeech(PendingLitanyCast cast)
    {
        AfterChantComplete(cast);
    }

    private void AfterChantComplete(PendingLitanyCast cast)
    {
        if (cast.ExtraDelay > TimeSpan.Zero)
        {
            cast.Stage = LitanyCastStage.ExtraDelay;
            if (!StartCastDoAfter(cast, cast.ExtraDelay, requireBook: cast.Origin == LitanyCastOrigin.Book && cast.Book != null))
            {
                ClearPending(cast, cancelled: true);
            }

            return;
        }

        TryCommit(cast);
    }

    private void TryCommit(PendingLitanyCast cast)
    {
        if (cast.Committed)
            return;

        cast.Stage = LitanyCastStage.Committing;

        // 1. Nonce/stage
        if (!TryComp(cast.Actor, out CruciformBearerComponent? bearer) ||
            bearer.PendingRequestId != cast.RequestId ||
            !_pendingByRequest.TryGetValue(cast.RequestId, out var live) ||
            !ReferenceEquals(live, cast))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // 2. Revalidate
        if (!_catalog.TryGetLitany(cast.LitanyId, out var litany) || !IsEffectivelyAvailable(litany))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (!_cruciform.TryGetCruciform(cast.Actor, out var cruciform, out var cruciformComp) ||
            cruciform != cast.Cruciform ||
            !IsEntitled(cruciformComp, litany))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (!_actionBlocker.CanSpeak(cast.Actor) || !IsPlayerActor(cast.Actor))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (cast.Origin == LitanyCastOrigin.Book)
        {
            if (cast.Book is not { } book ||
                !_hands.IsHolding(cast.Actor, book) ||
                !_hands.TryGetActiveItem(cast.Actor, out var activeBook) ||
                activeBook != book)
            {
                ClearPending(cast, cancelled: true);
                return;
            }
        }

        if (!IsCooldownAvailable(cast.Actor, bearer, litany, out _))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // 3. No multi-resource reservations for M3 stub.
        // 4. Effect plan: M3 no-op success stub when available and no real handler.
        if (!IsEffectivelyAvailable(litany))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // 5. Debit once, apply cooldown, commit stub effect synchronously.
        if (cast.Cost > 0 && !_cruciform.TrySpend(cast.Actor, cast.Cost))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        ApplyCooldown(bearer, litany);
        cast.Committed = true;

        // M3: no-op success stub. Real handlers land in later milestones.
        // A second completion must no-op because Committed is set.
        ClearPending(cast, cancelled: false);
    }

    private void ApplyCooldown(CruciformBearerComponent bearer, LitanyPrototype litany)
    {
        if (litany.CooldownScope == LitanyCooldownScope.None || litany.CooldownDuration <= TimeSpan.Zero)
            return;

        var until = _timing.CurTime + litany.CooldownDuration;
        if (litany.CooldownScope == LitanyCooldownScope.Personal)
        {
            bearer.PersonalCooldowns[litany.CooldownKey] = until;
            return;
        }

        _globalCooldowns[litany.CooldownKey] = until;
    }

    private void ClearPending(PendingLitanyCast cast, bool cancelled)
    {
        _pendingByRequest.Remove(cast.RequestId);

        if (TryComp(cast.Actor, out CruciformBearerComponent? bearer) &&
            bearer.PendingRequestId == cast.RequestId)
        {
            bearer.PendingRequestId = null;
            Dirty(cast.Actor, bearer);
        }

        cast.AwaitingBookSpeech = false;
        cast.DoAfterId = null;
    }

    private void ExpireStaleCasts()
    {
        if (_pendingByRequest.Count == 0)
            return;

        var now = _timing.CurTime;
        List<string>? expired = null;
        foreach (var (id, cast) in _pendingByRequest)
        {
            if (cast.Committed)
                continue;

            var orphaned = !TryComp(cast.Actor, out CruciformBearerComponent? bearer) ||
                           bearer.PendingRequestId != cast.RequestId;
            if (!orphaned && now <= cast.ExpiresAt)
                continue;

            expired ??= new List<string>();
            expired.Add(id);
        }

        if (expired == null)
            return;

        foreach (var id in expired)
        {
            if (_pendingByRequest.TryGetValue(id, out var cast))
                ClearPending(cast, cancelled: true);
        }
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _pendingByRequest.Clear();
        _rateByActor.Clear();
        _globalCooldowns.Clear();
        _availabilityOverrides.Clear();
        _testingTreatAsActor.Clear();
        _requestNonce = 0;
    }

    private bool TryRateLimit(EntityUid actor, bool isBegin, out LitanyActionResult failure)
    {
        failure = LitanyActionResult.Ok();
        var now = _timing.CurTime;
        if (!_rateByActor.TryGetValue(actor, out var state))
        {
            state = new ActorRateState { WindowStart = now };
            _rateByActor[actor] = state;
        }

        if (now - state.WindowStart >= TimeSpan.FromSeconds(1))
        {
            state.WindowStart = now;
            state.RequestsInWindow = 0;
        }

        if (state.RequestsInWindow >= MaxRequestsPerSecond)
        {
            failure = LitanyActionResult.Fail("oxyd-litany-denied-rate-limit");
            return false;
        }

        state.RequestsInWindow++;

        if (isBegin)
        {
            if (now - state.LastBegin < TimeSpan.FromSeconds(1) && state.LastBegin != default)
            {
                failure = LitanyActionResult.Fail("oxyd-litany-denied-rate-limit");
                return false;
            }

            state.LastBegin = now;
        }

        return true;
    }
}
