using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Skills;

[RegisterComponent, NetworkedComponent,AutoGenerateComponentState]
public sealed partial class  MobSkillComponent : Component
{
    /// <summary>
    /// Skill Prototype -> int[0 = base stat, 1 = boost amount]
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public Dictionary<ProtoId<SkillPrototype>, int[]> skills = new();

    public Dictionary<string, List<BuffData>> buffSources = new();

    public class BuffData
    {
        public int amount = 0;
        public TimeSpan expires = TimeSpan.MaxValue;
        public ProtoId<SkillPrototype> skill;
    }
}

[RegisterComponent]
public sealed partial class SkillOnEatComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, int> skills = new();
    [DataField]
    public TimeSpan duration = TimeSpan.MaxValue;
    [DataField] 
    public string buffId = "";
}
