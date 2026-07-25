using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Objectives;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class EatFoodObjectiveComponent : Component
{
    [DataField]
    public List<EntProtoId> foods = new();
}
