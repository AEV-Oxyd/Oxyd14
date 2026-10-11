using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    internal void TestingHandleSpeech(EntitySpokeEvent args) => OnSpeechAccepted(args);

    [SubscribeLocalEvent]
    private void OnSpeechAccepted(EntitySpokeEvent args)
    {
        // EntitySpokeEvent is only raised for accepted local Speak/Whisper.
        // Radio-prefix speech is rewritten onto the whisper path with a channel set.
        if (args.Channel != null)
            return;

        if (!IsPlayerActor(args.Source))
            return;

        if (TryComp(args.Source, out CruciformBearerComponent? bearer) &&
            !string.IsNullOrEmpty(bearer.PendingRequestId) &&
            TryComp(args.Source, out LitanyPendingCastComponent? pendingComp) &&
            pendingComp.Cast.RequestId == bearer.PendingRequestId &&
            pendingComp.Cast.AwaitingBookSpeech)
        {
            var pending = pendingComp.Cast;
            // Book casts speak server-generated text: match the pre-accent original,
            // which is the phrase this cast sent, so accents cannot cancel it.
            if (!LitanyPhraseParser.TryMatchExact(args.OriginalMessage, pending.Phrase))
                return;

            pending.AwaitingBookSpeech = false;
            ContinueAfterBookSpeech(pending);
            return;
        }

        if (TryHandleCeremonySpeech(args.Source, args))
            return;

        TryBeginFromManualSpeech(args);
    }

    private void TryBeginFromManualSpeech(EntitySpokeEvent args)
    {
        if (!_cruciform.IsActiveBearer(args.Source))
            return;

        if (!TryMatchSpeechToLitany(args, out var matched, out var spokenName))
            return;

        TryBeginLitany(args.Source, matched.ID, LitanyCastOrigin.ManualSpeech, spokenName: spokenName);
    }

    private bool TryMatchSpeechToLitany(EntitySpokeEvent args, out LitanyPrototype matched, out string? spokenName)
    {
        matched = null!;
        spokenName = null;

        // Fast path: normalized spoken / original against the phrase index.
        var spoken = LitanyPhraseParser.Normalize(args.Message);
        var original = LitanyPhraseParser.Normalize(args.OriginalMessage);

        if (_catalog.TryMatchPhrase(spoken, out var bySpoken))
        {
            // Non-stutter chants must match spoken; stutter-exception chants may match either.
            if (!bySpoken.IgnoreStuttering || LitanyPhraseParser.TryMatchExact(spoken, bySpoken.Phrase))
            {
                matched = bySpoken;
                return true;
            }
        }

        if (_catalog.TryMatchPhrase(original, out var byOriginal) && byOriginal.IgnoreStuttering)
        {
            matched = byOriginal;
            return true;
        }

        // Targeted placeholder phrases (Atonement/Penance/Excommunication) are not in the
        // exact phrase map; parse them for recognition denials even when unavailable.
        foreach (var litany in _catalog.EnumerateCatalog())
        {
            var compare = litany.IgnoreStuttering ? original : spoken;
            if (LitanyPhraseParser.TryParseTargetName(compare, litany.Phrase, out var name))
            {
                spokenName = name;
                matched = litany;
                return true;
            }
        }

        return false;
    }

    /// <summary>The name a speaker would use for <paramref name="target"/>.</summary>
    private string VisibleName(EntityUid actor, EntityUid target)
    {
        return Identity.Name(target, EntityManager, actor);
    }

    /// <summary>
    /// Speaks the named rite with the chosen follower's name in place of
    /// <c>[Target human]</c> once the cast has exactly one target.
    /// </summary>
    private string PhraseForTargets(EntityUid actor, string phrase, IReadOnlyList<EntityUid> targets)
    {
        var normalized = LitanyPhraseParser.Normalize(phrase);
        if (targets.Count != 1 || !LitanyPhraseParser.HasTargetPlaceholder(normalized))
            return normalized;

        return LitanyPhraseParser.WithTargetName(normalized, VisibleName(actor, targets[0]));
    }

    /// <summary>
    /// Keeps the one follower whose identity or true name equals <paramref name="spokenName"/>.
    /// Zero or two matches fail closed so a named rite cannot splash the whole list.
    /// </summary>
    private bool TryKeepNamedTarget(EntityUid actor, string spokenName, List<EntityUid> targets)
    {
        EntityUid? match = null;
        foreach (var target in targets)
        {
            if (!NameMatches(actor, target, spokenName))
                continue;

            if (match != null)
                return false;

            match = target;
        }

        if (match is not { } chosen)
            return false;

        targets.Clear();
        targets.Add(chosen);
        return true;
    }

    private bool NameMatches(EntityUid actor, EntityUid target, string spokenName)
    {
        if (string.Equals(VisibleName(actor, target), spokenName, StringComparison.Ordinal))
            return true;

        if (string.Equals(MetaData(target).EntityName, spokenName, StringComparison.Ordinal))
            return true;

        return TryComp<IdentityComponent>(target, out var identity) &&
               identity.IdentityEntitySlot?.ContainedEntity is { } ident &&
               string.Equals(MetaData(ident).EntityName, spokenName, StringComparison.Ordinal);
    }
}
