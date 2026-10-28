using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>
/// Book click speaks the litany phrase after a short chant, the same way Eris
/// ritual books call <c>say()</c>. The cruciform hears that phrase and casts.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class LitanyBookSpeakDoAfterEvent : SimpleDoAfterEvent
{
    public string LitanyId { get; }

    public LitanyBookSpeakDoAfterEvent(string litanyId)
    {
        LitanyId = litanyId;
    }

    public override DoAfterEvent Clone() => new LitanyBookSpeakDoAfterEvent(LitanyId);
}
