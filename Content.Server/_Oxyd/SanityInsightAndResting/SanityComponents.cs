namespace Content.Server._Oxyd.SanityInsightAndResting;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class SanityComponent : Component
{
    [ViewVariables]
    public float Sanity = 100f;
    [DataField]
    public float MaxSanity = 100f;
    [DataField]
    public float MinSanity = 0f;

    [ViewVariables]
    public float Rest = 0f;

    [ViewVariables]
    public float Insight = 0f;
}

[RegisterComponent]
public sealed partial class SanityInfluencerComponent : Component
{
    [DataField]
    public float sanityDelta = 1f;
}
