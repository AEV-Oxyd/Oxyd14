using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.SanityInsightAndResting;

/// <summary>
/// Gained skills without selecting a oddity
/// </summary>
public sealed class FocusedInternallyEvent : EntityEventArgs
{
    public required Dictionary<ProtoId<SkillPrototype>, int> skills;
}
