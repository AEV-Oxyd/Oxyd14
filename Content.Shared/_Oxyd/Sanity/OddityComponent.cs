using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Sanity;

/// <summary>Eris oddity inspiration stats (skill → bonus). Used by rest focus and artist bench.</summary>
[RegisterComponent]
public sealed partial class OddityComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, int> giving = new();
}
