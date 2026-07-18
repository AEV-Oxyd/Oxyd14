using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Skills;

[RegisterComponent]
public sealed partial class MobSkillComponent : Component
{
    /// <summary>
    /// Skill Prototype -> int[0 = base stat, 1 = boost amount]
    /// </summary>
    public Dictionary<ProtoId<SkillPrototype>, int[]> skills = new();
}
