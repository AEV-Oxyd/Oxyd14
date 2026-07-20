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
    public int max = 200;

    [DataField]
    public int min = -50;
}
