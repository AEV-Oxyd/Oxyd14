using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Tracks wound pain, temporary pain, and active analgesics. Pain does not add wounds.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true),
 AutoGenerateComponentPause]
public sealed partial class PainComponent : Component
{
    /// <summary>Recomputed pain level — the only value other systems read.</summary>
    [DataField, AutoNetworkedField]
    public float CurrentPain;

    [DataField, AutoNetworkedField]
    public float TemporaryPain;

    [DataField]
    public float RecoveryPerSecond = 0.5f;

    [DataField]
    public float TemporaryPainMultiplier = 1.33f;

    [DataField]
    public float SlowdownThreshold = 50f;

    [DataField]
    public float SevereThreshold = 100f;

    /// <summary>Damage types that count toward wound pain (Eris halloss sources).</summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> WoundPainTypes = new()
    {
        "Blunt", "Slash", "Piercing", "Heat", "Cold", "Shock",
    };

    [DataField, AutoNetworkedField]
    public bool Numb;

    /// <summary>Seconds between pain recomputations.</summary>
    [DataField]
    public float UpdateInterval = 1f;

    [AutoPausedField]
    public TimeSpan NextUpdate;
}
