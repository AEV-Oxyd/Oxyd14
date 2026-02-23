using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydGunChargeupComponent : Component
{
    [DataField, AutoNetworkedField]
    public float charge = 0f;

    [DataField]
    public float chargeToMultRatio = 1f;

    [DataField, AutoNetworkedField]
    public float maxCharge = 0f;

    [ViewVariables]
    public TimeSpan lastCharge = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public TimeSpan chargeDecayBegin = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public TimeSpan decayDelay = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan lastDecay = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public float amountPerDecay = 0f;


}

[RegisterComponent]
public sealed partial class ActiveOxydGunChargeupComponent : Component;
