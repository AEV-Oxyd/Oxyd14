using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.SanityInsightAndResting;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OddityComponent : Component
{
    public Dictionary<ProtoId<SkillPrototype>, int> giving =  new Dictionary<ProtoId<SkillPrototype>, int>();
}
