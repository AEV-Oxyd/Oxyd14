using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    // P4.1 target resolution (TryResolveTargets).
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

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
        SendProgressToActor(cast);
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

        // Speak without ignoreActionBlocker; radio prefixes disabled so the phrase is local.
        // TrySendInGameICMessage raises EntitySpokeEvent synchronously on success.
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

        // 3. No multi-resource reservations for Packet B medical effects.
        // 4. Effect plan: real handlers validate before debit; others no-op when available.
        if (!IsEffectivelyAvailable(litany))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        var hasHandler = LitanyHandlerCatalog.HasHandler(litany.Effect);
        if (hasHandler && !_effects.TryValidateEffects(cast.Actor, litany, out _, cast.Targets,
                cast.SelectedTokens, cast.SelectedText, cast.Designation))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        // 5. Debit once, apply the effect, then cool down only on success. An unexpected
        // apply failure refunds the debit, so a failed cast is atomic.
        if (cast.Cost > 0 && !_cruciform.TrySpend(cast.Actor, cast.Cost))
        {
            ClearPending(cast, cancelled: true);
            return;
        }

        if (hasHandler && !_effects.TryApplyEffects(cast.Actor, litany, cast.Targets,
                cast.SelectedTokens, cast.SelectedText, cast.Designation))
        {
            if (cast.Cost > 0)
                _cruciform.Refund(cast.Actor, cast.Cost);

            SendResultToActor(cast.Actor, LitanyActionResult.Fail("oxyd-litany-effect-failed", cast.RequestId));
            RefreshActorSnapshot(cast.Actor);
            ClearPending(cast, cancelled: false);
            return;
        }

        ApplyCooldown(bearer, litany);
        cast.Committed = true;
        SendResultToActor(cast.Actor, LitanyActionResult.Ok(cast.RequestId));
        RefreshActorSnapshot(cast.Actor);

        // Unimplemented available effects keep the historical no-op success stub.
        // A second completion must no-op because Committed is set.
        ClearPending(cast, cancelled: false);
    }

    /// <summary>
    /// Applies a book-UI selection to a Choosing cast: parses the opaque tokens, narrows the
    /// target list, stores the designation/text, revalidates the effects and starts the chant.
    /// Every rejection clears the pending cast, so a failed choice never leaves a live request.
    /// </summary>
    private LitanyActionResult SubmitChoicesCore(
        EntityUid actor,
        string requestId,
        List<string> tokens,
        string? recipeId,
        string? plainText,
        EntityUid? expectBook)
    {
        if (!string.IsNullOrEmpty(recipeId))
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");

        if (string.IsNullOrEmpty(requestId) ||
            !_pendingByRequest.TryGetValue(requestId, out var cast) ||
            cast.Actor != actor ||
            !cast.AwaitingChoice ||
            cast.Stage != LitanyCastStage.Choosing)
        {
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        if (expectBook is { } book && cast.Book != book)
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        if (_timing.CurTime > cast.ChoiceExpiresAt)
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-expired");
        }

        if (!_catalog.TryGetLitany(cast.LitanyId, out var litany) || !IsEffectivelyAvailable(litany))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-unavailable-feature");
        }

        var targetIndex = -1;
        ProtoId<NeoTheologyProfilePrototype>? designation = null;
        foreach (var token in tokens)
        {
            if (token.StartsWith("t:", StringComparison.Ordinal) &&
                int.TryParse(token.AsSpan(2), out var index))
            {
                if (targetIndex >= 0 || designation is not null)
                {
                    ClearPending(cast, cancelled: true);
                    return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
                }

                targetIndex = index;
                continue;
            }

            if (token.StartsWith("d:", StringComparison.Ordinal))
            {
                if (targetIndex >= 0 || designation is not null)
                {
                    ClearPending(cast, cancelled: true);
                    return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
                }

                designation = new ProtoId<NeoTheologyProfilePrototype>(token[2..]);
                continue;
            }

            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        var wantsTarget = cast.ChoiceTargets.Count > 0;
        if ((targetIndex >= 0) != wantsTarget || (wantsTarget && targetIndex >= cast.ChoiceTargets.Count))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        var wantsDesignation = cast.ChoiceDesignations.Count > 0;
        if (wantsDesignation != designation.HasValue ||
            (wantsDesignation && !cast.ChoiceDesignations.Contains(designation!.Value)))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        var text = plainText?.Trim();
        if (!cast.ChoiceAllowsPlainText && !string.IsNullOrEmpty(text))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        if (cast.ChoiceAllowsPlainText && string.IsNullOrEmpty(text))
        {
            // Eris returns without casting when the message is empty.
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-cancelled");
        }

        if (text is { Length: > MaxChoicePlainTextLength })
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-choice-invalid");
        }

        if (wantsTarget)
            cast.Targets = new List<EntityUid> { cast.ChoiceTargets[targetIndex] };

        cast.SelectedTokens = tokens;
        cast.SelectedText = text;
        cast.Designation = designation;
        cast.AwaitingChoice = false;

        if (LitanyHandlerCatalog.HasHandler(litany.Effect) &&
            !_effects.TryValidateEffects(cast.Actor, litany, out var effectFail, cast.Targets,
                cast.SelectedTokens, cast.SelectedText, cast.Designation))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail(effectFail ?? "oxyd-litany-no-effect", requestId);
        }

        var now = _timing.CurTime;
        var chantDuration = LitanyPhraseParser.BookChantDuration(cast.Phrase);
        cast.Stage = LitanyCastStage.Chanting;
        cast.StartedAt = now;
        cast.ChantEndsAt = now + chantDuration;
        cast.ExpiresAt = now + chantDuration + cast.ExtraDelay + CastGrace;

        if (!StartCastDoAfter(cast, chantDuration, requireBook: cast.Book is not null))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-denied-doafter", requestId);
        }

        return LitanyActionResult.Ok(requestId);
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
        _bookViewers.Clear();
        _testingLastSnapshot.Clear();
        TestingSnapshotSendCount = 0;
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

    /// <summary>
    /// Modes that succeed with zero candidates. These broadcast to "whoever is
    /// eligible" rather than selecting a specific target, so an empty list is a valid
    /// outcome (Eris Entreaty always succeeds, even with no other followers).
    /// Every mode not listed here fails closed on zero candidates.
    /// </summary>
    private static readonly FrozenSet<LitanyTargetMode> EmptyTolerantModes = new HashSet<LitanyTargetMode>
    {
        LitanyTargetMode.StationFollower,
    }.ToFrozenSet();

    /// <summary>
    /// Resolves the target candidates for a litany's <see cref="LitanyTargetMode"/>.
    /// Called once when the cast begins; commit replays the recorded list instead of
    /// re-resolving. Returns false when the mode resolved no candidates — the caller
    /// fails the begin with <paramref name="reason"/> — except for the broadcast modes
    /// in <see cref="EmptyTolerantModes"/>, where zero recipients is a success.
    /// </summary>
    public bool TryResolveTargets(
        EntityUid actor,
        LitanyPrototype proto,
        out List<EntityUid> targets,
        out LocId? reason)
    {
        targets = [];
        reason = "oxyd-litany-no-target";

        switch (proto.TargetMode)
        {
            case LitanyTargetMode.None:
                reason = null;
                return true;
            case LitanyTargetMode.Self:
                targets = [actor];
                reason = null;
                return true;
            case LitanyTargetMode.AdjacentLiving:
                targets = ResolveAdjacentMobs(actor, proto, followersOnly: false);
                break;
            case LitanyTargetMode.AdjacentFollower:
                targets = ResolveAdjacentMobs(actor, proto, followersOnly: true);
                break;
            case LitanyTargetMode.VisibleFollower:
                targets = _effects.CollectVisibleActiveFollowers(actor, _effects.GetSenseRange(proto));
                break;
            case LitanyTargetMode.StationFollower:
                targets = new List<EntityUid>(_effects.EnumerateSameStationActiveFollowers(actor));
                break;
            case LitanyTargetMode.FrontMachine:
                targets = ResolveFacedTileMachines(actor, proto);
                break;
            case LitanyTargetMode.NearbyMachine:
                targets = ResolveNearbyMachines(actor, proto);
                break;
            case LitanyTargetMode.VisibleArea:
                targets = ResolveVisibleMobs(actor, proto);
                break;
            case LitanyTargetMode.FrontTile:
                // ponytail: FrontTile must resolve to a coordinate, not an entity, for the
                // construction packet (P4.13). Fail closed until that path exists.
                return false;
            case LitanyTargetMode.Ceremony:
                // P4.11 owns the ceremony participant ring; fail closed until then.
                return false;
            default:
                return false;
        }

        // Deterministic order: the recorded list must not depend on lookup hash order.
        targets.Sort();
        if (targets.Count == 0 && !EmptyTolerantModes.Contains(proto.TargetMode))
            return false;

        reason = null;
        return true;
    }

    /// <summary>
    /// Living mobs on the actor's own tile and the tile they face. Range bounds the
    /// lookup circle (1 m default); the tile gate is what makes "adjacent" mean the
    /// tile in front. Followers-only additionally requires an active cruciform.
    /// §7.1: Dead mobs are excluded unless the litany is a dead-only flow (extraction,
    /// resurrection) that declares <see cref="LitanyEffect.AllowsDeadTarget"/>.
    /// </summary>
    private List<EntityUid> ResolveAdjacentMobs(EntityUid actor, LitanyPrototype proto, bool followersOnly)
    {
        var results = new List<EntityUid>();
        if (!TryGetFrontTiles(actor, out var ownTile, out var frontTile))
            return results;

        var allowDead = proto.Effects.Any(effect => effect.AllowsDeadTarget);

        var range = proto.Range > 0 ? proto.Range : 1f;
        foreach (var (mob, _) in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(actor).Coordinates, range))
        {
            if (mob == actor)
                continue;
            if (!_mobState.IsAlive(mob) && !(allowDead && _mobState.IsDead(mob)))
                continue;
            if (followersOnly && !_cruciform.IsActiveBearer(mob))
                continue;
            if (!IsOnTile(mob, ownTile) && !IsOnTile(mob, frontTile))
                continue;

            results.Add(mob);
        }

        return results;
    }

    /// <summary>
    /// Machine-ish marker for FrontMachine/NearbyMachine: the NeoTheology machines
    /// built in P2 (machines.yml) plus the holy door. Effect handlers narrow to the
    /// machine they need; add a component here when a new machine litany lands.
    /// </summary>
    private bool IsLitanyMachine(EntityUid uid)
    {
        return HasComp<NeoTheologyDoorComponent>(uid) ||
               HasComp<NeoTheologyAltarComponent>(uid) ||
               HasComp<BioreactorComponent>(uid) ||
               HasComp<BiogeneratorComponent>(uid) ||
               HasComp<CruciformForgeComponent>(uid) ||
               HasComp<CruciformClonerComponent>(uid) ||
               HasComp<CruciformReaderComponent>(uid) ||
               HasComp<ArmamentsPrinterComponent>(uid) ||
               HasComp<EyeOfTheProtectorComponent>(uid);
    }

    /// <summary>Machines on the tile the actor faces.</summary>
    private List<EntityUid> ResolveFacedTileMachines(EntityUid actor, LitanyPrototype proto)
    {
        var results = new List<EntityUid>();
        if (!TryGetFrontTiles(actor, out _, out var frontTile))
            return results;

        var range = proto.Range > 0 ? proto.Range : 1.5f;
        foreach (var uid in _lookup.GetEntitiesInRange(Transform(actor).Coordinates, range))
        {
            if (uid != actor && IsLitanyMachine(uid) && IsOnTile(uid, frontTile))
                results.Add(uid);
        }

        return results;
    }

    /// <summary>Machines within the litany range (1.5 m default) on the actor's map.</summary>
    private List<EntityUid> ResolveNearbyMachines(EntityUid actor, LitanyPrototype proto)
    {
        var results = new List<EntityUid>();
        var actorXform = Transform(actor);
        var range = proto.Range > 0 ? proto.Range : 1.5f;
        foreach (var uid in _lookup.GetEntitiesInRange(actorXform.Coordinates, range))
        {
            if (uid == actor || !IsLitanyMachine(uid))
                continue;
            if (Transform(uid).MapID != actorXform.MapID)
                continue;

            results.Add(uid);
        }

        return results;
    }

    /// <summary>Every mob with line of sight inside the litany range.</summary>
    private List<EntityUid> ResolveVisibleMobs(EntityUid actor, LitanyPrototype proto)
    {
        var results = new List<EntityUid>();
        var range = _effects.GetSenseRange(proto);
        foreach (var (mob, _) in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(actor).Coordinates, range))
        {
            if (mob == actor)
                continue;
            if (!_examine.InRangeUnOccluded(actor, mob, range, predicate: null))
                continue;

            results.Add(mob);
        }

        return results;
    }

    /// <summary>The actor's tile and the tile their local rotation faces.</summary>
    private bool TryGetFrontTiles(EntityUid actor, out Vector2i ownTile, out Vector2i frontTile)
    {
        ownTile = default;
        frontTile = default;
        if (!TryComp(actor, out TransformComponent? xform))
            return false;

        var coords = _xform.ToMapCoordinates(xform.Coordinates);
        if (coords.MapId == MapId.Nullspace)
            return false;

        var pos = coords.Position;
        ownTile = pos.Floored();
        frontTile = (pos + xform.LocalRotation.ToVec()).Floored();
        return true;
    }

    private bool IsOnTile(EntityUid uid, Vector2i tile)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        var coords = _xform.ToMapCoordinates(xform.Coordinates);
        return coords.MapId != MapId.Nullspace && coords.Position.Floored() == tile;
    }
}
