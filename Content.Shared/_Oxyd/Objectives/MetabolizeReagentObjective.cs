using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Objectives;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class MetabolizeReagentObjectiveComponent : Component
{
    public List<ProtoId<ReagentPrototype>> reagents;
    public int metabolizeTimes = 15;
    public int metabolized = 0;
}

[RegisterComponent]
public sealed partial class MetabolizeReagentObjectiveTrackerComponent : Component
{
    public List<Entity<MetabolizeReagentObjectiveComponent>> origin = new();
}
