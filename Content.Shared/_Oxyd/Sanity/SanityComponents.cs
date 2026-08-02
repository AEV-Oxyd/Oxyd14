using Content.Shared.EntityEffects;
using Robust.Shared.GameStates;

namespace Content.Server._Oxyd.SanityInsightAndResting;

public enum SanIndex : int
{
    damageToInsight = 0, // index for sanity damage to insight conversion
    deltaMult = 1, // multiplier for sanity damage/gain
    damageCap = 2, // limit to how low this type of damage can bring sanity to

}

[Flags]
public enum SanitySource : byte
{
    Environmental = 0,
    Witness = 1 << 0,
    Actor = 1 << 1,
    Chemical = 1 << 2,
    Mental = 1 << 3,
    Belief = 1 << 4
}
/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(false, true)]
public sealed partial class SanityComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Sanity = 100f;
    [DataField, AutoNetworkedField]
    public float MaxSanity = 100f;
    [DataField, AutoNetworkedField]
    public float MinSanity = 0f;

    [DataField]
    public Dictionary<SanitySource, float[]> modifiers = new();

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Insight = 0f;

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int RestAccumulated = 0;

    [AutoNetworkedField, ViewVariables]
    public Dictionary<EntityUid,float> desireProg = new();

    [AutoNetworkedField, ViewVariables]
    public Dictionary<EntityUid, string> desireDesc = new();
}


public abstract partial class SanityInfluencerComponent : Component
{
    [DataField]
    public float sanityDelta = 1f;

    [DataField]
    public SanitySource sanityType = SanitySource.Environmental;
}

[RegisterComponent]
public sealed partial class InfluenceSanityOnViewComponent : SanityInfluencerComponent;

// goes on the food
[RegisterComponent]
public sealed partial class InfluenceSanityOnTasteComponent : SanityInfluencerComponent;

public sealed partial class GiveSanityEffect : EntityEffectBase<GiveSanityEffect>
{
    [DataField]
    public float sanityDelta = 1f;

    [DataField]
    public SanitySource sanityType = SanitySource.Chemical;
}
