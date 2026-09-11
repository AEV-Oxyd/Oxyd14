using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P3.9: a bounded scrying session (Eris inquisitor scrying). The caster's eye is retargeted
/// onto an invisible marker at the target's position; the session lapses at <see cref="EndsAt"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScryingSessionComponent : Component
{
    /// <summary>The invisible entity the caster's eye is bound to for the session's duration.</summary>
    [DataField]
    public EntityUid? Marker;

    /// <summary>The eye target before the session starts.</summary>
    [DataField]
    public EntityUid? PreviousTarget;

    /// <summary>When the session lapses and the eye is restored to the caster.</summary>
    [DataField]
    public TimeSpan EndsAt;
}
