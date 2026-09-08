using Robust.Shared.GameObjects;

namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>
/// Raised server-side exactly once after local Speak/Whisper text has passed chat's
/// normal validation, sanitization, transformation, and delivery path.
/// </summary>
[ByRefEvent]
public readonly record struct LitanySpeechAcceptedEvent(
    EntityUid Source,
    string OriginalMessage,
    string SpokenMessage,
    LitanySpeechKind Kind,
    bool HadRadioChannelIntent,
    ulong Sequence);
