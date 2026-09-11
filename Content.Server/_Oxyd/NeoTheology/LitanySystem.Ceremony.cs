using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// The group-ritual engine (Eris <c>datum/ritual/group</c> and
/// <c>datum/core_module/group_ritual</c>) and the server half of the ceremony payloads.
/// </summary>
public sealed partial class LitanySystem
{
    /// <summary>
    /// Named divergence: Eris keeps the module until a wrong phrase, so a stalled rite lives
    /// forever. The fork ends the rite after five minutes.
    /// </summary>
    public static readonly TimeSpan CeremonyTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Eris <c>O.force_active = max(60, O.force_active)</c>.</summary>
    public static readonly TimeSpan SanctifyForceActiveTime = TimeSpan.FromSeconds(60);

    /// <summary>Eris <c>flash</c>: caster Weaken(10), others Weaken(5).</summary>
    private static readonly TimeSpan SearingSelfKnockdown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SearingVictimKnockdown = TimeSpan.FromSeconds(5);

    private static readonly ProtoId<SkillPrototype> VigilanceSkill = "Vig";

    [Dependency] private readonly SharedStunSystem _stun = default!;

    private void InitializeCeremony()
    {
        SubscribeLocalEvent<CruciformBearerComponent, LitanySanctifyAreaEvent>(OnSanctifyArea);
        SubscribeLocalEvent<CruciformBearerComponent, LitanyGrantLitanySetEvent>(OnGrantLitanySet);
        SubscribeLocalEvent<CruciformBearerComponent, LitanyToggleDiscipleHudEvent>(OnToggleDiscipleHud);
        SubscribeLocalEvent<CruciformBearerComponent, LitanySearingRevelationEvent>(OnSearingRevelation);
    }

    /// <summary>
    /// True when the litany is a ceremony that only a priest or inquisitor may start
    /// (Eris <c>/datum/ritual/group/cruciform/high_ritual</c>).
    /// </summary>
    private static bool CeremonyRequiresClergy(LitanyPrototype litany)
    {
        return litany.Effects.OfType<LitanyCeremonyEffect>().Any(effect => effect.RequiresClergy);
    }

    /// <summary>
    /// Server commit for a <see cref="LitanyTargetMode.Ceremony"/> cast: the rite opens and the
    /// followers join by speaking the phrase list. Eris never spends power and never runs an
    /// effect at this point.
    /// </summary>
    private bool TryStartCeremony(
        PendingLitanyCast cast,
        LitanyPrototype litany,
        CruciformBearerComponent bearer,
        out LocId? failure)
    {
        if (litany.CeremonyPhrases.Count < 2)
        {
            failure = "oxyd-litany-effect-failed";
            return false;
        }

        if (!_cruciform.TryGetCruciform(cast.Actor, out _, out var cruciform) ||
            (CeremonyRequiresClergy(litany) && !LitanyEffectSystem.IsClergyProfile(cruciform.Profile)))
        {
            failure = "oxyd-litany-ceremony-clergy";
            return false;
        }

        var ceremony = EnsureComp<ActiveCeremonyComponent>(cast.Actor);
        ceremony.Ritual = litany.ID;
        ceremony.Cruciform = cast.Cruciform;
        ceremony.Phrases = new List<string>(litany.CeremonyPhrases);
        ceremony.First = true;
        ceremony.Participants.Clear();
        ceremony.CorrectParticipants.Clear();
        ceremony.Range = _effects.GetSenseRange(litany);
        ceremony.ExpiresAt = _timing.CurTime + CeremonyTimeout;

        ApplyCooldown(bearer, litany);
        cast.Committed = true;
        SendResultToActor(cast.Actor, LitanyActionResult.Ok(cast.RequestId));
        RefreshActorSnapshot(cast.Actor);
        ClearPending(cast, cancelled: false);

        failure = null;
        return true;
    }

    /// <summary>
    /// Routes one accepted speech event through the live ceremonies. Returns true when the rite
    /// consumed the phrase, so manual litany recognition does not also fire on it.
    /// </summary>
    private bool TryHandleCeremonySpeech(EntityUid speaker, EntitySpokeEvent args)
    {
        // Eris hear(): the speaker must carry a cruciform.
        if (!_cruciform.IsActiveBearer(speaker))
            return false;

        var candidates = new List<(EntityUid Starter, ActiveCeremonyComponent Ceremony)>();
        var query = EntityQueryEnumerator<ActiveCeremonyComponent, TransformComponent>();
        while (query.MoveNext(out var starter, out var ceremony, out var starterXform))
        {
            if (TerminatingOrDeleted(starter))
                continue;

            if (speaker != starter)
            {
                if (!TryComp(speaker, out TransformComponent? speakerXform) ||
                    speakerXform.MapID != starterXform.MapID ||
                    (speakerXform.WorldPosition - starterXform.WorldPosition).Length() > ceremony.Range)
                    continue;

                if (!ceremony.First && !ceremony.Participants.Contains(speaker))
                    continue;
            }

            candidates.Add((starter, ceremony));
        }

        // Deterministic order: a speaker inside two rings must land in the same one every time.
        candidates.Sort((left, right) => left.Starter.Id.CompareTo(right.Starter.Id));

        foreach (var (starter, ceremony) in candidates)
        {
            if (!_catalog.TryGetLitany(ceremony.Ritual, out var litany))
            {
                RemComp<ActiveCeremonyComponent>(starter);
                return true;
            }

            if (speaker == starter)
            {
                var matched = PhraseMatches(args, ceremony.Phrases[1]);
                if (ceremony.Phrases.Count > 2)
                {
                    if (matched && CeremonyStepCheck(litany, starter))
                        AdvanceCeremony(starter, ceremony);
                    else
                        FailCeremony(starter, ceremony);
                }
                else if (matched && CeremonyStepCheck(litany, starter) && ceremony.Participants.Count > 0)
                {
                    CompleteCeremony(starter, ceremony, litany);
                }
                else
                {
                    FailCeremony(starter, ceremony);
                }

                return true;
            }

            if (CeremonyStepCheck(litany, speaker) &&
                !ceremony.CorrectParticipants.Contains(speaker) &&
                PhraseMatches(args, ceremony.Phrases[0]))
            {
                ceremony.CorrectParticipants.Add(speaker);
            }
            else
            {
                ceremony.Participants.Remove(speaker);
            }

            return true;
        }

        return false;
    }

    /// <summary>Eris <c>next_phrase()</c>: drop the spoken phrase and promote the correct followers.</summary>
    private void AdvanceCeremony(EntityUid starter, ActiveCeremonyComponent ceremony)
    {
        ceremony.Phrases.RemoveAt(0);
        ceremony.First = false;
        ceremony.Participants = ceremony.CorrectParticipants;
        ceremony.CorrectParticipants = new HashSet<EntityUid>();

        _effects.DeliverSocialNotice(starter,
            Loc.GetString("oxyd-litany-ceremony-continue", ("count", ceremony.Participants.Count)));
    }

    /// <summary>
    /// Eris <c>trigger_success</c>: run the payload for the starter and every follower, then end
    /// the rite. Dead followers drop out at the final round; the recorded count is what the
    /// effects use.
    /// </summary>
    private void CompleteCeremony(EntityUid starter, ActiveCeremonyComponent ceremony, LitanyPrototype litany)
    {
        var targets = new List<EntityUid> { starter };
        foreach (var participant in ceremony.Participants)
        {
            if (participant != starter && _mobState.IsAlive(participant))
                targets.Add(participant);
        }

        targets.Sort();
        var participantCount = targets.Count - 1;

        if (_effects.TryApplyEffects(starter, litany, targets, ceremonyParticipants: participantCount))
        {
            ConsumeCeremonyMiraclePoint(litany);
            foreach (var target in targets)
                _effects.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-ceremony-success"));
        }
        else
        {
            foreach (var target in targets)
                _effects.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-ceremony-fail"));
        }

        RemComp<ActiveCeremonyComponent>(starter);
    }

    /// <summary>Eris <c>trigger_fail</c>: the module is removed and everyone hears the failure.</summary>
    private void FailCeremony(EntityUid starter, ActiveCeremonyComponent ceremony)
    {
        var message = Loc.GetString("oxyd-litany-ceremony-fail");
        _effects.DeliverSocialNotice(starter, message);
        foreach (var participant in ceremony.Participants)
            _effects.DeliverSocialNotice(participant, message);

        RemComp<ActiveCeremonyComponent>(starter);
    }

    /// <summary>
    /// Eris <c>stat/step_check</c>: miracle points gate the stat rites. The fork banks the points
    /// on the Eye of the Protector and spends one per completed rite, together with the +25
    /// observation.
    /// </summary>
    private bool CeremonyStepCheck(LitanyPrototype litany, EntityUid speaker)
    {
        if (litany.Effects.OfType<LitanySanctifyEffect>().Any())
            return true;

        if (litany.Effects.OfType<LitanyGroupStatEffect>().Any())
            return HasMiraclePoint();

        return HasActiveObeliskInRange(speaker);
    }

    private bool HasMiraclePoint()
    {
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out _, out var eye))
        {
            if (eye.MiraclePoints > 0)
                return true;
        }

        return false;
    }

    private void ConsumeCeremonyMiraclePoint(LitanyPrototype litany)
    {
        if (!litany.Effects.OfType<LitanyCeremonyEffect>().Any(effect => effect.ConsumesMiraclePoint))
            return;

        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out var eye, out var comp))
        {
            if (comp.MiraclePoints <= 0)
                continue;

            comp.MiraclePoints--;
            comp.Observation = Math.Clamp(comp.Observation + 25f, comp.MinObservation, comp.MaxObservation);
            Dirty(eye, comp);
            return;
        }
    }

    /// <summary>Eris default <c>step_check</c>: an active obelisk inside its radius and seven tiles.</summary>
    private bool HasActiveObeliskInRange(EntityUid speaker)
    {
        var xform = Transform(speaker);
        var query = EntityQueryEnumerator<ObeliskComponent, TransformComponent>();
        while (query.MoveNext(out _, out var obelisk, out var obeliskXform))
        {
            if (!obelisk.Active || obeliskXform.MapID != xform.MapID)
                continue;

            var distance = (obeliskXform.WorldPosition - xform.WorldPosition).Length();
            if (distance <= obelisk.Radius && distance <= 7f)
                return true;
        }

        return false;
    }

    private static bool PhraseMatches(EntitySpokeEvent args, string phrase)
    {
        if (LitanyPhraseParser.TryMatchExact(args.Message, phrase))
            return true;

        return args.OriginalMessage != args.Message && LitanyPhraseParser.TryMatchExact(args.OriginalMessage, phrase);
    }

    private void ExpireCeremonies()
    {
        var expired = new List<EntityUid>();
        var query = EntityQueryEnumerator<ActiveCeremonyComponent>();
        while (query.MoveNext(out var starter, out var ceremony))
        {
            if (_timing.CurTime >= ceremony.ExpiresAt ||
                !_mobState.IsAlive(starter) ||
                !_cruciform.TryGetCruciform(starter, out _, out _))
            {
                expired.Add(starter);
            }
        }

        foreach (var starter in expired)
        {
            if (TryComp<ActiveCeremonyComponent>(starter, out var ceremony))
                FailCeremony(starter, ceremony);
        }
    }

    private int GetSkillValue(EntityUid uid, ProtoId<SkillPrototype> skill)
    {
        if (!TryComp<MobSkillComponent>(uid, out var skills) ||
            !skills.skills.TryGetValue(skill, out var value) ||
            value.Length == 0)
            return 0;

        var total = value[0];
        if (value.Length > 1)
            total += value[1];

        return total;
    }

    /// <summary>Test helper: the live rite started by <paramref name="starter"/>.</summary>
    public bool TestingTryGetCeremony(EntityUid starter, out ActiveCeremonyComponent? ceremony)
    {
        return TryComp(starter, out ceremony);
    }

    private void OnSanctifyArea(EntityUid uid, CruciformBearerComponent bearer, ref LitanySanctifyAreaEvent args)
    {
        // Eris loops every obelisk and pushes force_active to at least sixty seconds.
        var until = _timing.CurTime + SanctifyForceActiveTime;
        var query = EntityQueryEnumerator<ObeliskComponent>();
        while (query.MoveNext(out _, out var obelisk))
        {
            if (obelisk.ForceActiveUntil < until)
                obelisk.ForceActiveUntil = until;
        }

        args.Handled = true;
    }

    private void OnGrantLitanySet(EntityUid uid, CruciformBearerComponent bearer, ref LitanyGrantLitanySetEvent args)
    {
        if (!_cruciform.TryGetCruciform(args.Target, out _, out var cruciform))
            return;

        if (!cruciform.GrantedSets.Add(args.Set))
            return;

        _cruciform.RecomputeProfile(args.Target, cruciform);
        RefreshActorSnapshot(args.Target);
        args.Handled = true;
    }

    private void OnToggleDiscipleHud(EntityUid uid, CruciformBearerComponent bearer, ref LitanyToggleDiscipleHudEvent args)
    {
        if (HasComp<NtDiscipleHudComponent>(args.User))
            RemComp<NtDiscipleHudComponent>(args.User);
        else
            EnsureComp<NtDiscipleHudComponent>(args.User);

        args.Handled = true;
    }

    private void OnSearingRevelation(EntityUid uid, CruciformBearerComponent bearer, ref LitanySearingRevelationEvent args)
    {
        // Eris: prob(100 - STAT_VIG) knocks the caster down for ten seconds. Eris Weaken has no
        // gravity rule, so the fall is forced: a weightless mob still goes down (named divergence).
        if (ProbPercent(100 - GetSkillValue(args.User, VigilanceSkill)))
            _stun.TryKnockdown(args.User, SearingSelfKnockdown, force: true);

        var xform = Transform(args.User);
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var victim, out _, out var victimXform))
        {
            if (victim == args.User || victimXform.MapID != xform.MapID)
                continue;

            if ((victimXform.WorldPosition - xform.WorldPosition).Length() > args.Range)
                continue;

            if (!_examine.InRangeUnOccluded(args.User, victim, args.Range, predicate: null))
                continue;

            if (!_mobState.IsAlive(victim) || _cruciform.IsActiveBearer(victim))
                continue;

            if (ProbPercent(100 - GetSkillValue(victim, VigilanceSkill)))
                _stun.TryKnockdown(victim, SearingVictimKnockdown, force: true);
        }

        args.Handled = true;
    }

    private bool ProbPercent(int percent)
    {
        return _effects.Prob(Math.Clamp(percent, 0, 100) / 100f);
    }
}
