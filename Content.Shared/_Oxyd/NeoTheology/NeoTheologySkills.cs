using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Skill ids the NeoTheology systems and effects share, so one copy defines each value.
/// </summary>
public static class NeoTheologySkills
{
    /// <summary>Eris <c>STAT_VIG</c>, the resistance skill for Searing Revelation.</summary>
    public static readonly ProtoId<SkillPrototype> Vigilance = "Vig";

    /// <summary>Eris <c>STAT_COG</c>, cognition — feeds cruciform regeneration.</summary>
    public static readonly ProtoId<SkillPrototype> Cognition = "Cog";

    /// <summary>Eris <c>STAT_TGH</c>, toughness — Call to Battle grants it.</summary>
    public static readonly ProtoId<SkillPrototype> Toughness = "Tgh";

    /// <summary>Eris <c>STAT_ROB</c>, robustness — Call to Battle grants it.</summary>
    public static readonly ProtoId<SkillPrototype> Robustness = "Rob";
}
