using Content.Shared.Nutrition;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Objectives;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class ExperienceFlavourObjectiveComponent : Component
{
    [DataField]
    public List<ProtoId<FlavorPrototype>> flavours = new();

    [DataField]
    public int timesToExperience = 15;

    [ViewVariables]
    public int experienced = 0;
}
[RegisterComponent]
public sealed partial class ExperienceFlavourObjectiveTrackerComponent : Component
{
    public List<Entity<ExperienceFlavourObjectiveComponent>> origin = new();
}
