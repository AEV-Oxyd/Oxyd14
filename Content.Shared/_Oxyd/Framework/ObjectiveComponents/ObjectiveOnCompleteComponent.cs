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
public sealed partial class ObjectiveGiveRestComponent : Component
{
    public float mod = 1f;
    public int baseG = 2;
    public int topG = 10;
}

[RegisterComponent]
public sealed partial class ObjectiveGiveInsightComponent : Component
{
    [DataField]
    public float amount = 0;
}
