using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Chat;

namespace Content.Server._Oxyd.NeoTheology;

public sealed partial class LitanySystem
{
    public void TestingHandleSpeech(EntitySpokeEvent args) => OnSpeechAccepted(args);

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
            _pendingByRequest.TryGetValue(bearer.PendingRequestId, out var pending) &&
            pending.Actor == args.Source &&
            pending.AwaitingBookSpeech)
        {
            var compare = ResolveCompareText(pending.LitanyId, args);
            if (!LitanyPhraseParser.TryMatchExact(compare, pending.Phrase))
                return;

            pending.AwaitingBookSpeech = false;
            ContinueAfterBookSpeech(pending);
            return;
        }

        TryBeginFromManualSpeech(args);
    }

    private void TryBeginFromManualSpeech(EntitySpokeEvent args)
    {
        if (!_cruciform.IsActiveBearer(args.Source))
            return;

        if (!TryMatchSpeechToLitany(args, out var matched))
            return;

        TryBeginLitany(args.Source, matched.ID, LitanyCastOrigin.ManualSpeech);
    }

    private bool TryMatchSpeechToLitany(EntitySpokeEvent args, out LitanyPrototype matched)
    {
        matched = null!;

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
            if (LitanyPhraseParser.TryParseTargetName(compare, litany.Phrase, out _))
            {
                matched = litany;
                return true;
            }
        }

        return false;
    }

    private string ResolveCompareText(string litanyId, EntitySpokeEvent args)
    {
        if (_catalog.TryGetLitany(litanyId, out var litany) && litany.IgnoreStuttering)
            return args.OriginalMessage;

        return args.Message;
    }
}
