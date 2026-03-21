namespace Content.Shared._Oxyd.OxydGunSystem;

/// </summary>
[RegisterComponent]
public sealed partial class GlowOnChargeComponent : Component
{
    [DataField]
    public float minCharge = 0.3f;

    [DataField]
    public float maxCharge = 1f;

    [DataField]
    public float minRadius = 1f;

    [DataField]
    public float maxRadius = 8f;

    [DataField]
    public float minPower = 2f;

    [DataField]
    public float maxPower = 10f;
}
