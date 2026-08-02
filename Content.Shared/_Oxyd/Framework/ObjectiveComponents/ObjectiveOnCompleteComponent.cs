using Content.Shared.Mind;

namespace Content.Shared._Oxyd.Framework.Objectives;

/// <summary>
/// Monitor & Raise Event
/// </summary>
[RegisterComponent]
public sealed partial class ObjectiveEventOnCompleteComponent : Component
{
    [ViewVariables]
    public Entity<MindComponent> mind;
}

[RegisterComponent]
public sealed partial class ObjectiveGiveRestComponent : Component;

[RegisterComponent]
public sealed partial class ObjectiveGiveInsightComponent : Component
{
    [DataField]
    public float amount = 0;
}
