using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Skills;

[RegisterComponent, NetworkedComponent,AutoGenerateComponentState]
public sealed partial class MobSkillComponent : Component
{
    /// <summary>
    /// Skill Prototype -> int[0 = base stat, 1 = boost amount]
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public Dictionary<ProtoId<SkillPrototype>, int[]> skills = new();
}
