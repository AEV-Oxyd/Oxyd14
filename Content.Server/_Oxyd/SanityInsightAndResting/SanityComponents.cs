namespace Content.Server._Oxyd.SanityInsightAndResting;

public enum SanIndex : int
{
    damageToInsight = 0, // index for sanity damage to insight conversion
    deltaMult = 1, // multiplier for sanity damage/gain
    damageCap = 2, // limit to how low this type of damage can bring sanity to

}
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

    [DataField]
    public Dictionary<SanityDamageSource, float[]> modifiers = new();

    [ViewVariables]
    public float Rest = 0f;

    [ViewVariables]
    public float Insight = 0f;

    [ViewVariables]
    public int RestAccumulated = 0;
}

[RegisterComponent]
public sealed partial class SanityInfluencerComponent : Component
{
    [DataField]
    public float sanityDelta = 1f;

    [DataField]
    public SanityDamageSource sanityType = SanityDamageSource.Environmental;
}
