using Content.Shared.Chat;

namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>
/// Server-raised hook for a successfully accepted local Speak/Whisper utterance.
/// This is never a client network event. <see cref="EntitySpokeEvent"/> remains
/// unchanged for other listeners; this carries both original and transformed text
/// plus the original channel intent needed to reject radio-prefix speech.
/// </summary>
public sealed class LitanySpeechAcceptedEvent : EntityEventArgs
{
    public EntityUid Source { get; }
    public string OriginalMessage { get; }
    public string SpokenMessage { get; }
    public LitanySpeechKind SpeechKind { get; }
    public ulong Sequence { get; }

    /// <summary>
    /// The caller-requested IC chat type before radio rewriting. Emote/OOC never
    /// raise this event; Speak/Whisper that were rewritten onto a radio channel set
    /// <see cref="RadioTransmitted"/>.
    /// </summary>
    public InGameICChatType DesiredType { get; }

    /// <summary>
    /// True when the accepted utterance was transmitted as a radio message
    /// (radio-prefix path). Must be rejected by litany recognition.
    /// </summary>
    public bool RadioTransmitted { get; }

    public LitanySpeechAcceptedEvent(
        EntityUid source,
        string originalMessage,
        string spokenMessage,
        LitanySpeechKind speechKind,
        ulong sequence,
        InGameICChatType desiredType,
        bool radioTransmitted)
    {
        Source = source;
        OriginalMessage = originalMessage;
        SpokenMessage = spokenMessage;
        SpeechKind = speechKind;
        Sequence = sequence;
        DesiredType = desiredType;
        RadioTransmitted = radioTransmitted;
    }
}
