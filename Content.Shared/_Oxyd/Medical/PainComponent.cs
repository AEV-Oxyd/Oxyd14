using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Tracks wound pain, temporary pain, and active analgesics. Pain does not add wounds.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PainComponent : Component
{
    [DataField, AutoNetworkedField]
    public float CurrentPain;

    [DataField]
    public float TemporaryPain;

    [DataField]
    public float RecoveryPerSecond = 0.5f;

    [DataField]
    public float TemporaryPainMultiplier = 1.33f;

    [DataField, AutoNetworkedField]
    public float SlowdownThreshold = 50f;

    [DataField, AutoNetworkedField]
    public float SevereThreshold = 100f;

    [DataField]
    public bool Numb;

    [DataField]
    public Dictionary<string, AnalgesicDose> Analgesics = new();

    [DataField]
    public float UpdateRemaining;
}

[DataDefinition]
public sealed partial class AnalgesicDose
{
    [DataField]
    public float Strength;

    [DataField]
    public float Remaining;
}
