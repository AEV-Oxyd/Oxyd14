using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Systems;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.NeoTheology.UI;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Speech recognition and the server cast transaction state machine.
/// Cast lifecycle lives in LitanySystem.Casting.cs, ceremonies in
/// LitanySystem.Ceremony.cs, speech matching in LitanySystem.Speech.cs,
/// book/viewer UI in LitanySystem.UI.cs.
/// </summary>
public sealed partial class LitanySystem : EntitySystem
{
    public const int MaxRequestsPerSecond = 5;
    public static readonly TimeSpan CastGrace = TimeSpan.FromSeconds(5);

    /// <summary>How long a book cast waits for the caster's choice before it expires.</summary>
    public static readonly TimeSpan ChoiceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Server limit for a Sending message, matching the client edit limit.</summary>
    public const int MaxChoicePlainTextLength = 512;

    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private LitanyEffectSystem _effects = default!;
    [Dependency] private LitanyPrototypeValidationSystem _catalog = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SanitySystem _sanity = default!;

    private ulong _requestNonce;

    /// <summary>
    /// Round-global litany state lives on a dedicated nullspace entity so nothing about
    /// entities or rounds is stored on the system itself. The entity is (re)created at
    /// round restart by <see cref="OnRoundCleanup"/>; this only re-derives it if missing.
    /// </summary>
    private LitanyGlobalStateComponent GlobalState
    {
        get
        {
            var query = EntityQueryEnumerator<LitanyGlobalStateComponent>();
            return query.MoveNext(out _, out var comp) ? comp : EnsureGlobalState();
        }
    }

    private LitanyGlobalStateComponent EnsureGlobalState()
    {
        var ent = Spawn(null, MapCoordinates.Nullspace);
        return EnsureComp<LitanyGlobalStateComponent>(ent);
    }

    private Dictionary<string, TimeSpan> GlobalCooldowns => GlobalState.Cooldowns;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<LitanyBookComponent>(LitanyUiKey.Book, subs =>
        {
            subs.Event<BeginLitanyMessage>(OnBeginLitanyMessage);
            subs.Event<CancelLitanyMessage>(OnCancelLitanyMessage);
        });
    }

    [SubscribeLocalEvent]
    private void OnLitanySanityDelta(Entity<SanityComponent> ent, ref LitanySanityDeltaEvent args)
    {
        if (args.Handled)
            return;

        _sanity.ApplySanityDelta(ent, SanitySource.Belief, args.Amount);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ExpireStaleCasts();
        ExpireCeremonies();
        RefreshOpenViewers();
    }

    /// <summary>
    /// Test helper: force a litany ID to be treated as available without shipping
    /// <c>enabled: true</c> in production YAML. Dependency-gated rows stay unavailable.
    /// </summary>
    internal void TestingSetAvailabilityOverride(string litanyId, bool available)
    {
        GlobalState.AvailabilityOverrides[litanyId] = available;
    }

    internal void TestingClearAvailabilityOverrides()
    {
        GlobalState.AvailabilityOverrides.Clear();
    }

    /// <summary>Test helper: drops round-global cooldowns so recast contracts can be exercised.</summary>
    internal void TestingClearCooldowns()
    {
        GlobalState.Cooldowns.Clear();
    }

    /// <summary>
    /// Integration-test helper so disconnected fixtures can exercise player-only
    /// cast paths without a real <see cref="ActorComponent"/> session.
    /// </summary>
    internal void TestingTreatAsActor(EntityUid uid)
    {
        EnsureComp<LitanyTestingActorComponent>(uid);
    }

    internal void TestingClearActors()
    {
        var query = EntityQueryEnumerator<LitanyTestingActorComponent>();
        while (query.MoveNext(out var uid, out _))
            RemCompDeferred<LitanyTestingActorComponent>(uid);
    }

    private bool IsPlayerActor(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid) || HasComp<LitanyTestingActorComponent>(uid);
    }

    internal bool TestingTryGetPending(string requestId, [NotNullWhen(true)] out PendingLitanyCast? cast)
    {
        var query = EntityQueryEnumerator<LitanyPendingCastComponent>();
        while (query.MoveNext(out _, out var pending))
        {
            if (!pending.Cast.Cleared && pending.Cast.RequestId == requestId)
            {
                cast = pending.Cast;
                return true;
            }
        }

        cast = null;
        return false;
    }

    internal int TestingPendingCount
    {
        get
        {
            // Component removal is deferred to end-of-tick; cleared casts don't count.
            var count = 0;
            var query = EntityQueryEnumerator<LitanyPendingCastComponent>();
            while (query.MoveNext(out _, out var pending))
            {
                if (!pending.Cast.Cleared)
                    count++;
            }
            return count;
        }
    }

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
        string? choiceToken = null,
        string? spokenName = null)
    {
        var result = BeginLitanyCore(actor, litanyId, origin, book, expectedRevision, choiceToken, spokenName);
        SendResultToActor(actor, result, isFinal: !result.Success);
        return result;
    }

    private LitanyActionResult BeginLitanyCore(
        EntityUid actor,
        ProtoId<LitanyPrototype> litanyId,
        LitanyCastOrigin origin,
        EntityUid? book = null,
        uint? expectedRevision = null,
        string? choiceToken = null,
        string? spokenName = null)
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

        // Named rites (Atonement, Penance, Excommunication) keep the spoken identity.
        // A missing or ambiguous name fails before any debit.
        if (origin == LitanyCastOrigin.ManualSpeech &&
            LitanyPhraseParser.HasTargetPlaceholder(litany.Phrase) &&
            (string.IsNullOrWhiteSpace(spokenName) ||
             !TryKeepNamedTarget(actor, spokenName.Trim(), resolvedTargets)))
        {
            return LitanyActionResult.Fail("oxyd-litany-no-target");
        }

        if (!string.IsNullOrEmpty(choiceToken))
            return LitanyActionResult.Fail("oxyd-litany-denied-invalid-choice");

        if (!IsCooldownAvailable(actor, bearer, litany, out var cooldownFail))
            return cooldownFail;

        var holiness = _cruciform.GetHoliness(actor);
        if (litany.Cost > 0 && !NeoTheologyHoliness.CanAfford(holiness, litany.Cost, _cruciform.GetDebitTolerance()))
            return LitanyActionResult.Fail("oxyd-litany-no-cost");

        if (litany.Effects.Count > 0 &&
            !_effects.TryValidateEffects(actor, litany, out var effectFail, resolvedTargets))
            return LitanyActionResult.Fail(effectFail ?? "oxyd-litany-no-effect");

        var requestId = NextRequestId();
        var now = _timing.CurTime;
        var phrase = PhraseForTargets(actor, litany.Phrase, resolvedTargets);
        var chantDuration = LitanyPhraseParser.BookChantDuration(phrase);

        // Both spoken and book prayers use the private choice surface. A named target
        // narrows recipients, but does not bypass designation/blueprint/text choices.
        var offersTargetChoice = litany.SelectTarget && resolvedTargets.Count > 1;
        var offersDesignationChoice = litany.DesignationChoices.Count > 0;
        var offersBlueprintChoice = litany.SelectBlueprint;
        var needsChoice = offersTargetChoice || offersDesignationChoice || offersBlueprintChoice || litany.AllowPlainText;

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

        if (litany.RequiresHeldOddity &&
            _effects.TryGetHeldOddity(actor, out var heldOddity, out _))
            cast.HeldOddity = heldOddity;

        foreach (var target in resolvedTargets)
        {
            if (_cruciform.TryGetCruciformEntity(target, out var targetImplant, out _))
                cast.TargetCruciforms[target] = targetImplant;
        }

        EnsureComp<LitanyPendingCastComponent>(actor).Cast = cast;
        bearer.PendingRequestId = requestId;
        Dirty(actor, bearer);

        if (needsChoice)
        {
            if (origin == LitanyCastOrigin.ManualSpeech)
                OpenPrayerPrompt(cast);
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

        if (!TryComp(actor, out LitanyPendingCastComponent? pending) ||
            pending.Cast.RequestId != requestId)
        {
            return LitanyActionResult.Fail("oxyd-litany-denied-unknown-request");
        }

        var cast = pending.Cast;

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

        if (GlobalState.AvailabilityOverrides.TryGetValue(litany.ID, out var forced))
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

        if (GlobalCooldowns.TryGetValue(key, out var globalUntil) && globalUntil > now)
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
        return TryComp(actor, out LitanyPendingCastComponent? pending) &&
               !pending.Cast.Cleared && !pending.Cast.Committed;
    }
}

/// <summary>Server-only pending cast record. Never networked.</summary>
public sealed class PendingLitanyCast
{
    [ViewVariables] public string RequestId = string.Empty;
    [ViewVariables] public EntityUid Actor;
    [ViewVariables] public EntityUid Cruciform;
    [ViewVariables] public EntityUid? Book;
    [ViewVariables] public EntityUid? Prompt;
    [ViewVariables] public string LitanyId = string.Empty;
    [ViewVariables] public LitanyCastOrigin Origin;

    /// <summary>
    /// Targets resolved once at begin time (P4.1). Commit revalidates and applies to
    /// this exact list — it never re-resolves, so a mid-chant move cannot retarget.
    /// </summary>
    [ViewVariables] public List<EntityUid> Targets = new();
    [ViewVariables] public Dictionary<EntityUid, EntityUid> TargetCruciforms = new();
    [ViewVariables] public EntityUid? HeldOddity;

    [ViewVariables] public LitanyCastStage Stage;
    [ViewVariables] public string Phrase = string.Empty;
    [ViewVariables] public double Cost;
    [ViewVariables] public string CooldownKey = string.Empty;
    [ViewVariables] public LitanyCooldownScope CooldownScope;
    [ViewVariables] public TimeSpan CooldownDuration;
    [ViewVariables] public TimeSpan ExtraDelay;
    [ViewVariables] public TimeSpan StartedAt;
    [ViewVariables] public TimeSpan ChantEndsAt;
    [ViewVariables] public TimeSpan ExpiresAt;
    [ViewVariables] public bool AwaitingBookSpeech;
    [ViewVariables] public bool Committed;
    [ViewVariables] public DoAfterId? DoAfterId;

    /// <summary>True while the cast waits for the caster's book-UI selection.</summary>
    [ViewVariables] public bool AwaitingChoice;
    /// <summary>Set once the cast reaches a terminal state so cleanup never runs twice.</summary>
    [ViewVariables] public bool Cleared;
    [ViewVariables] public TimeSpan ChoiceExpiresAt;
    [ViewVariables] public List<EntityUid> ChoiceTargets = new();
    [ViewVariables] public List<ProtoId<NeoTheologyProfilePrototype>> ChoiceDesignations = new();
    [ViewVariables] public List<ProtoId<NeoTheologyBlueprintPrototype>> ChoiceBlueprints = new();
    [ViewVariables] public bool ChoiceAllowsPlainText;
    [ViewVariables] public List<string> SelectedTokens = new();
    [ViewVariables] public string? SelectedText;
    [ViewVariables] public ProtoId<NeoTheologyProfilePrototype>? Designation;
    [ViewVariables] public ProtoId<NeoTheologyBlueprintPrototype>? SelectedBlueprint;
}
