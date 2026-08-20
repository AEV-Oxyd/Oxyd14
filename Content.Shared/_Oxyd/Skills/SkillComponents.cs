using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Skills;

[RegisterComponent, NetworkedComponent,AutoGenerateComponentState]
public sealed partial class  MobSkillComponent : Component
{
    /// <summary>
    /// Skill Prototype -> int[0 = base stat, 1 = boost amount]
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public Dictionary<ProtoId<SkillPrototype>, int[]> skills = new();

    [ViewVariables]
    public Dictionary<ProtoId<SkillPrototype>, Dictionary<string,List<BuffData>>> buffSources = new();

    [Serializable,NetSerializable]
    public class BuffData
    {
        [ViewVariables]
        public int amount = 0;
        [ViewVariables]
        public TimeSpan expires = TimeSpan.MaxValue;
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
