using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Sanity;

/// <summary>
/// Gained skills without selecting a oddity
/// </summary>
public sealed class FocusedInternallyEvent : EntityEventArgs
{
    public required Dictionary<ProtoId<SkillPrototype>, int> skills;
}

[Serializable, NetSerializable]
public sealed class RequestInternalFocus : EntityEventArgs;
