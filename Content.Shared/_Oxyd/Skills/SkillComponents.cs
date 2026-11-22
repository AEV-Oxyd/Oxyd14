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

/// <summary>Eris /obj/item/device/mental_imprinter (spawn_blacklisted loot):
/// the user picks a skill and stabs the imprinter into their eye for a stat
/// boost at heavy sanity cost. The verb menu is the port of Eris's input()
/// skill picker; a low base stat earns a doubled boost for half the toll.</summary>
[RegisterComponent]
public sealed partial class OxydMentalImprinterComponent : Component
{
    /// <summary>Eris sanity toll for the imprint (30; halved on the boosted path).</summary>
    [DataField] public float SanityDamage = 30f;

    /// <summary>Eris stat_increase for a normal imprint.</summary>
    [DataField] public int NormalGain = 5;

    /// <summary>Eris doubled stat_increase when the base stat is at or below
    /// <see cref="LowSkillThreshold"/> (Eris STAT_LEVEL_BASIC).</summary>
    [DataField] public int BoostedGain = 10;

    /// <summary>Eris STAT_LEVEL_BASIC: base stats at or below this qualify for
    /// the doubled boost and halved sanity toll.</summary>
    [DataField] public int LowSkillThreshold = 15;
}
