using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>
/// Completion event for a server-owned litany DoAfter. The request ID is an
/// opaque correlation value; target and authority state remain in the server's
/// pending cast record and are revalidated when the event is handled.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class LitanyDoAfterEvent : SimpleDoAfterEvent
{
    public string RequestId { get; }

    public LitanyDoAfterEvent(string requestId)
    {
        RequestId = requestId;
    }

    public override DoAfterEvent Clone() => new LitanyDoAfterEvent(RequestId);
}
