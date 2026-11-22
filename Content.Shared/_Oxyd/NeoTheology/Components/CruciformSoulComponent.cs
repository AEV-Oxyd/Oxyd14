using Content.Shared.Preferences;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Chemistry.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// The soul a cruciform keeps. <see cref="CoreModuleBehaviorSystem"/> writes it when the
/// cloning module is installed and again when it is removed; it lives on the cruciform, not
/// on the module, so pulling the module does not lose it — that is the resurrection path
/// (Eris <c>datum/core_module/cruciform/cloning</c>).
/// </summary>
/// <remarks>
/// This fork has no <c>HumanoidAppearanceComponent</c>, so the appearance half of the Eris
/// snapshot rides <see cref="Profile"/> (a <see cref="HumanoidCharacterProfile"/> carries
/// appearance, species, gender and age). Native languages, skills, blood chemistry and
/// flavor text are saved separately; temporary stat buffs retain their original deadlines.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class CruciformSoulComponent : Component
{
    [DataField] public bool HasSnapshot;
    [DataField] public string? Ckey;
    [DataField] public EntityUid? MindId;
    [DataField] public string Name = string.Empty;
    [DataField] public HumanoidCharacterProfile? Profile;
    [DataField] public int BiomassCost = 100;
    [DataField] public string? Dna;
    [DataField] public string? Fingerprint;
    [DataField] public EntityUid? SourceBody;
    [DataField] public EntityUid? PreparedBody;

    [DataField] public Dictionary<ProtoId<SkillPrototype>, int> BaseSkills = new();
    [DataField] public List<SoulSkillBuff> SkillBuffs = new();
    [DataField] public HashSet<ProtoId<LanguagePrototype>> Speaking = new();
    [DataField] public HashSet<ProtoId<LanguagePrototype>> Understanding = new();
    [DataField] public ProtoId<LanguagePrototype>? ChosenLanguage;
    [DataField] public Solution? BloodReference;
    [DataField] public string? FlavorText;
    [DataField] public bool HolyLight;

    /// <summary>The saved body's biological rejection, not the player's beliefs.</summary>
    [DataField] public bool AtheistMutation;
}

[DataDefinition]
public sealed partial class SoulSkillBuff
{
    [DataField] public ProtoId<SkillPrototype> Skill;
    [DataField] public string Source = string.Empty;
    [DataField] public int Amount;
    [DataField] public TimeSpan Expires;
}
