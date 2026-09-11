using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P3.1: the Eye of the Protector (Eris <c>obj/machinery/power/eotp</c>). It observes the faithful
/// in range, banks armament points for the printer, and is the religion's control point.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EyeOfTheProtectorComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Observation;

    [DataField]
    public float MaxObservation = 1800f;

    [DataField]
    public float MinObservation = -100f;

    [DataField, AutoNetworkedField]
    public int ArmamentsPoints;

    [DataField]
    public int MaxArmamentsPoints = 150;

    /// <summary>Eris <c>max_increase</c>: the first purchase of an armament lifts the ceiling.</summary>
    [ViewVariables]
    public bool FirstPurchaseMade;

    /// <summary>Per-armament purchase counters, keyed by prototype id (Eris keeps these on the EOTP datum).</summary>
    [ViewVariables]
    public Dictionary<string, int> PurchaseCount = new();

    [DataField]
    public float ObservationRadius = 20f;

    [DataField]
    public float ObservationPerFaithful = 20f;

    [DataField]
    public float ObservationPerNeutral = 10f;

    [DataField]
    public float ObservationPerFaithless = -15f;

    [DataField]
    public float ObservationPerObeliskKill = 5f;

    /// <summary>
    /// How long the in-range blessing lasts. Short enough that it lapses once a bearer walks out
    /// of the radius and the scan stops renewing it.
    /// </summary>
    [DataField]
    public TimeSpan FaithfulBlessingDuration = TimeSpan.FromSeconds(5);

    /// <summary>Eris <c>power_cooldown</c>: how often power accrues.</summary>
    [DataField]
    public TimeSpan PowerInterval = TimeSpan.FromMinutes(1);

    /// <summary>Eris <c>power_gaine</c> base: 2 + clamp(observation)/100 per interval.</summary>
    [DataField]
    public float PowerGainBase = 2f;

    /// <summary>Eris <c>max_power</c>: reaching it releases a miracle.</summary>
    [DataField]
    public float MaxPower = 120f;

    /// <summary>Eris <c>armaments_rate</c>: points added per released miracle.</summary>
    [DataField]
    public int ArmamentsRate = 125;

    /// <summary>Eris <c>power</c>: the accumulated miracle fuel.</summary>
    [ViewVariables]
    public float Power;

    [ViewVariables]
    public TimeSpan NextPowerUpdate;

    [DataField]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(5);

    /// <summary>Oddity prototypes the ODDITY miracle may spawn. Empty until a real oddity exists in-tree.</summary>
    [DataField]
    public List<EntProtoId> OddityRewards = new();

    /// <summary>UI cooldown: the next power update.</summary>
    [ViewVariables]
    public TimeSpan NextMiracle;

    [ViewVariables]
    public TimeSpan NextScan;

    [DataField]
    public TimeSpan RescanInterval = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public TimeSpan NextRescan;

    /// <summary>One shared record for Eye scans and obelisk scans. Values store the original awards.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, float> Scanned = new();
}
