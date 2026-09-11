using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Systems;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Speech recognition and the server cast transaction state machine.
/// Packet B/C handlers live in LitanySystem.Medical.cs / LitanySystem.Social.cs;
/// unimplemented available effects may still commit a no-op stub.
/// </summary>
public sealed partial class LitanySystem : EntitySystem
{
    public const int MaxRequestsPerSecond = 5;
    public const int MaxBeginsPerSecond = 1;
    public static readonly TimeSpan ChoiceExpiry = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CastGrace = TimeSpan.FromSeconds(5);

    /// <summary>How long a book cast waits for the caster's choice before it expires.</summary>
    public static readonly TimeSpan ChoiceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Server limit for a Sending message, matching the client edit limit.</summary>
    public const int MaxChoicePlainTextLength = 512;

    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly LitanyEffectSystem _effects = default!;
    [Dependency] private readonly LitanyPrototypeValidationSystem _catalog = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, PendingLitanyCast> _pendingByRequest = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityUid, ActorRateState> _rateByActor = new();
    private readonly Dictionary<string, TimeSpan> _globalCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _availabilityOverrides = new(StringComparer.Ordinal);
    private readonly HashSet<EntityUid> _testingTreatAsActor = new();

    private ulong _requestNonce;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpeechAccepted);
        SubscribeLocalEvent<LitanyDoAfterEvent>(OnLitanyDoAfter);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);

        Subs.BuiEvents<LitanyBookComponent>(LitanyUiKey.Book, subs =>
        {
            subs.Event<BeginLitanyMessage>(OnBeginLitanyMessage);
            subs.Event<CancelLitanyMessage>(OnCancelLitanyMessage);
        });

        InitializeUi();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ExpireStaleCasts();
    }

    /// <summary>
    /// Test helper: force a litany ID to be treated as available without shipping
    /// <c>enabled: true</c> in production YAML. Dependency-gated rows stay unavailable.
    /// </summary>
    public void TestingSetAvailabilityOverride(string litanyId, bool available)
    {
        _availabilityOverrides[litanyId] = available;
    }

    public void TestingClearAvailabilityOverrides()
    {
        _availabilityOverrides.Clear();
    }

    /// <summary>Test helper: drops round-global cooldowns so recast contracts can be exercised.</summary>
    public void TestingClearCooldowns()
    {
        _globalCooldowns.Clear();
    }

    /// <summary>
    /// Integration-test helper so disconnected fixtures can exercise player-only
    /// cast paths without a real <see cref="ActorComponent"/> session.
    /// </summary>
    public void TestingTreatAsActor(EntityUid uid)
    {
        _testingTreatAsActor.Add(uid);
    }

    public void TestingClearActors()
    {
        _testingTreatAsActor.Clear();
    }

    private bool IsPlayerActor(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid) || _testingTreatAsActor.Contains(uid);
    }

    public bool TestingTryGetPending(string requestId, [NotNullWhen(true)] out PendingLitanyCast? cast)
    {
        return _pendingByRequest.TryGetValue(requestId, out cast);
    }

    public int TestingPendingCount => _pendingByRequest.Count;

    /// <summary>
    /// Server entry for beginning a litany from speech or book UI. Cost/authority
    /// claims from the client are ignored; validation is server-authoritative.
    /// </summary>
    public LitanyActionResult TryBeginLitany(
        EntityUid actor,
        ProtoId<LitanyPrototype> litanyId,
        LitanyCastOrigin origin,
        EntityUid? book = null,
        uint? expectedRevision = null,
        string? choiceToken = null)
    {
        var result = BeginLitanyCore(actor, litanyId, origin, book, expectedRevision, choiceToken);
        SendResultToActor(actor, result);
        return result;
    }

    private LitanyActionResult BeginLitanyCore(
        EntityUid actor,
        ProtoId<LitanyPrototype> litanyId,
        LitanyCastOrigin origin,
        EntityUid? book = null,
        uint? expectedRevision = null,
        string? choiceToken = null)
    {
        if (!TryRateLimit(actor, isBegin: true, out var rateFail))
            return rateFail;

        if (!_catalog.CatalogReady)
            return LitanyActionResult.Fail("oxyd-litany-unavailable-feature");

        if (!IsPlayerActor(actor))
            return LitanyActionResult.Fail("oxyd-litany-denied-npc");

        if (!_cruciform.TryGetCruciform(actor, out var cruciform, out var cruciformComp) ||
            !TryComp(actor, out CruciformBearerComponent? bearer))
            return LitanyActionResult.Fail("oxyd-litany-denied-no-implant");

        if (expectedRevision is { } revision && bearer.UiRevision != revision)
        {
            RefreshSnapshotOnStaleRevision(actor, book);
            return LitanyActionResult.Fail("oxyd-litany-denied-stale-revision");
        }

        if (!_catalog.TryGetLitany(litanyId, out var litany))
            return LitanyActionResult.Fail("oxyd-litany-denied-unknown");

        if (!IsEffectivelyAvailable(litany))
            return LitanyActionResult.Fail(litany.UnavailableReason ?? "oxyd-litany-unavailable-feature");

        if (!IsEntitled(cruciformComp, litany))
            return LitanyActionResult.Fail("oxyd-litany-denied-entitlement");

        if (!_actionBlocker.CanSpeak(actor))
            return LitanyActionResult.Fail("oxyd-litany-denied-cannot-speak");

        if (!string.IsNullOrEmpty(bearer.PendingRequestId) || HasPendingForActor(actor))
            return LitanyActionResult.Fail("oxyd-litany-ui-busy");

        if (origin == LitanyCastOrigin.Book)
        {
            if (book is not { } bookUid || !HasComp<LitanyBookComponent>(bookUid))
                return LitanyActionResult.Fail("oxyd-litany-denied-no-book");
            if (!_hands.IsHolding(actor, bookUid) ||
                !_hands.TryGetActiveItem(actor, out var active) ||
                active != bookUid)
                return LitanyActionResult.Fail("oxyd-litany-denied-book-hand");
        }

        // Every mode resolves its candidates up front (P4.1).
        if (!TryResolveTargets(actor, litany, out var resolvedTargets, out var targetFail))
            return LitanyActionResult.Fail(targetFail ?? "oxyd-litany-no-target");

        // The blueprint catalog has no deterministic fallback: the caster must pick one
        // (Eris "Select construction"). Manual speech has no choice surface, so it fails closed.
        if (litany.SelectBlueprint && origin != LitanyCastOrigin.Book)
            return LitanyActionResult.Fail("oxyd-litany-book-required");

        if (!string.IsNullOrEmpty(choiceToken))
            return LitanyActionResult.Fail("oxyd-litany-denied-invalid-choice");

        if (!IsCooldownAvailable(actor, bearer, litany, out var cooldownFail))
            return cooldownFail;

        var holiness = _cruciform.GetHoliness(actor);
        if (litany.Cost > 0 && !NeoTheologyHoliness.CanAfford(holiness, litany.Cost, _cruciform.GetDebitTolerance()))
            return LitanyActionResult.Fail("oxyd-litany-no-cost");

        if (LitanyHandlerCatalog.HasHandler(litany.Effect) &&
            !_effects.TryValidateEffects(actor, litany, out var effectFail, resolvedTargets))
            return LitanyActionResult.Fail(effectFail ?? "oxyd-litany-no-effect");

        var requestId = NextRequestId();
        var now = _timing.CurTime;
        var phrase = LitanyPhraseParser.Normalize(litany.Phrase);
        var chantDuration = LitanyPhraseParser.BookChantDuration(phrase);

        // A book cast of a choice-requiring litany pauses in the Choosing stage. Manual
        // speech has no choice surface, so it keeps the deterministic fallback target.
        var offersTargetChoice = litany.SelectTarget && resolvedTargets.Count > 1;
        var offersDesignationChoice = litany.DesignationChoices.Count > 0;
        var offersBlueprintChoice = litany.SelectBlueprint;
        var needsChoice = origin == LitanyCastOrigin.Book &&
                          (offersTargetChoice || offersDesignationChoice || offersBlueprintChoice || litany.AllowPlainText);

        var cast = new PendingLitanyCast
        {
            RequestId = requestId,
            Actor = actor,
            Cruciform = cruciform,
            Book = origin == LitanyCastOrigin.Book ? book : null,
            LitanyId = litany.ID,
            Origin = origin,
            Targets = resolvedTargets,
            Stage = needsChoice ? LitanyCastStage.Choosing : LitanyCastStage.Chanting,
            Phrase = phrase,
            Cost = litany.Cost,
            CooldownKey = litany.CooldownKey,
            CooldownScope = litany.CooldownScope,
            CooldownDuration = litany.CooldownDuration,
            ExtraDelay = litany.ExtraDelay,
            StartedAt = now,
            ChantEndsAt = now + chantDuration,
            ExpiresAt = needsChoice
                ? now + ChoiceTimeout
                : now + chantDuration + litany.ExtraDelay + CastGrace,
            AwaitingBookSpeech = false,
            Committed = false,
        };

        if (needsChoice)
        {
            cast.AwaitingChoice = true;
            cast.ChoiceExpiresAt = cast.ExpiresAt;
            if (offersTargetChoice)
                cast.ChoiceTargets = resolvedTargets;
            if (offersDesignationChoice)
                cast.ChoiceDesignations = litany.DesignationChoices;
            if (offersBlueprintChoice)
                cast.ChoiceBlueprints = EnumerateBlueprintChoices();
            cast.ChoiceAllowsPlainText = litany.AllowPlainText;
        }

        _pendingByRequest[requestId] = cast;
        bearer.PendingRequestId = requestId;
        Dirty(actor, bearer);

        if (needsChoice)
        {
            SendChoiceSnapshot(cast);
            SendProgressToActor(cast);
            return LitanyActionResult.Ok(requestId);
        }

        if (origin == LitanyCastOrigin.Book)
        {
            // Book: chant DoAfter first, then emit speech, then optional extra delay, then commit.
            if (!StartCastDoAfter(cast, chantDuration, requireBook: true))
            {
                ClearPending(cast, cancelled: true);
                return LitanyActionResult.Fail("oxyd-litany-denied-doafter");
            }

            return LitanyActionResult.Ok(requestId);
        }

        // Manual speech already uttered; fairness DoAfter uses the same duration.
        if (!StartCastDoAfter(cast, chantDuration, requireBook: false))
        {
            ClearPending(cast, cancelled: true);
            return LitanyActionResult.Fail("oxyd-litany-denied-doafter");
        }

        return LitanyActionResult.Ok(requestId);
    }

    public LitanyActionResult TryCancelLitany(EntityUid actor, string requestId)
    {
        var result = CancelLitanyCore(actor, requestId);
        SendResultToActor(actor, result);
        return result;
    }

    private LitanyActionResult CancelLitanyCore(EntityUid actor, string requestId)
    {
        if (!TryRateLimit(actor, isBegin: false, out var rateFail))
            return rateFail;

        if (!_pendingByRequest.TryGetValue(requestId, out var cast) || cast.Actor != actor)
            return LitanyActionResult.Fail("oxyd-litany-denied-unknown-request");

        if (cast.Committed)
            return LitanyActionResult.Fail("oxyd-litany-denied-already-committed");

        if (cast.DoAfterId is { } doAfterId)
            _doAfter.Cancel(doAfterId);

        ClearPending(cast, cancelled: true);
        return LitanyActionResult.Fail("oxyd-litany-cancelled", requestId);
    }

    private bool IsEffectivelyAvailable(LitanyPrototype litany)
    {
        if (litany.Dependency != NeoTheologyDependency.None)
            return false;

        if (_availabilityOverrides.TryGetValue(litany.ID, out var forced))
            return forced;

        return litany.IsAvailable;
    }

    private static bool IsEntitled(CruciformComponent cruciform, LitanyPrototype litany)
    {
        if (litany.GrantedBy.Count == 0)
            return false;

        foreach (var set in litany.GrantedBy)
        {
            if (cruciform.UnlockedSets.Contains(set))
                return true;
        }

        return false;
    }

    private bool IsCooldownAvailable(
        EntityUid actor,
        CruciformBearerComponent bearer,
        LitanyPrototype litany,
        out LitanyActionResult failure)
    {
        failure = LitanyActionResult.Ok();
        if (litany.CooldownScope == LitanyCooldownScope.None || litany.CooldownDuration <= TimeSpan.Zero)
            return true;

        var key = litany.CooldownKey;
        var now = _timing.CurTime;
        if (litany.CooldownScope == LitanyCooldownScope.Personal)
        {
            if (bearer.PersonalCooldowns.TryGetValue(key, out var until) && until > now)
            {
                failure = LitanyActionResult.Fail("oxyd-litany-denied-cooldown");
                return false;
            }

            return true;
        }

        if (_globalCooldowns.TryGetValue(key, out var globalUntil) && globalUntil > now)
        {
            failure = LitanyActionResult.Fail("oxyd-litany-denied-cooldown");
            return false;
        }

        return true;
    }

    private string NextRequestId()
    {
        return $"litany-{++_requestNonce}-{_timing.CurTime.Ticks}";
    }

    private bool HasPendingForActor(EntityUid actor)
    {
        foreach (var cast in _pendingByRequest.Values)
        {
            if (cast.Actor == actor && !cast.Committed)
                return true;
        }

        return false;
    }
}

/// <summary>Server-only pending cast record. Never networked.</summary>
public sealed class PendingLitanyCast
{
    public string RequestId = string.Empty;
    public EntityUid Actor;
    public EntityUid Cruciform;
    public EntityUid? Book;
    public string LitanyId = string.Empty;
    public LitanyCastOrigin Origin;

    /// <summary>
    /// Targets resolved once at begin time (P4.1). Commit revalidates and applies to
    /// this exact list — it never re-resolves, so a mid-chant move cannot retarget.
    /// </summary>
    public List<EntityUid> Targets = new();

    public LitanyCastStage Stage;
    public string Phrase = string.Empty;
    public double Cost;
    public string CooldownKey = string.Empty;
    public LitanyCooldownScope CooldownScope;
    public TimeSpan CooldownDuration;
    public TimeSpan ExtraDelay;
    public TimeSpan StartedAt;
    public TimeSpan ChantEndsAt;
    public TimeSpan ExpiresAt;
    public bool AwaitingBookSpeech;
    public bool Committed;
    public DoAfterId? DoAfterId;

    /// <summary>True while the cast waits for the caster's book-UI selection.</summary>
    public bool AwaitingChoice;
    public TimeSpan ChoiceExpiresAt;
    public List<EntityUid> ChoiceTargets = new();
    public List<ProtoId<NeoTheologyProfilePrototype>> ChoiceDesignations = new();
    public List<ProtoId<NeoTheologyBlueprintPrototype>> ChoiceBlueprints = new();
    public bool ChoiceAllowsPlainText;
    public List<string> SelectedTokens = new();
    public string? SelectedText;
    public ProtoId<NeoTheologyProfilePrototype>? Designation;
    public ProtoId<NeoTheologyBlueprintPrototype>? SelectedBlueprint;
}

internal sealed class ActorRateState
{
    public TimeSpan WindowStart;
    public int RequestsInWindow;
    public TimeSpan LastBegin;
}
