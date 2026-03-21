namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class RecoilHandlerComponent : Component
{
    [DataField]
    public float lossPerTick = 1f;

    [ViewVariables]
    public float currentRecoil = 0f;

    [DataField]
    public float maxRecoil = 100f;

    [DataField]
    public Angle maxDeviation = Angle.FromDegrees(5);
}

[RegisterComponent]
public sealed partial class ActiveRecoilHandlerComponent : Component
{
}

