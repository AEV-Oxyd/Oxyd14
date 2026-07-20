using Content.Shared.Tools;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Skills;

[Prototype("Skill")]
public sealed partial class SkillPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public string name = "generic skill";

    [DataField]
    public string description = "a generic skill , it has no description because someone forgot to SET ONE!!!";

    [DataField]
    public List<ProtoId<ToolQualityPrototype>> affectingQualities = new();

    [DataField]
    // how much delay is added/reduced per tool level + skill level above 0
    public float timeIncrements = 0.05f;

    [DataField]
    public int max = 200;

    [DataField]
    public int min = -50;
}
