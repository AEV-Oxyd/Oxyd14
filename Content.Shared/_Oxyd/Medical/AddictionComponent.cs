using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Records exposure and dependence separately from the blood's current reagent contents.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class AddictionComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, ReagentDependence> Reagents = new();

    [AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.FromSeconds(10);

    [DataField]
    public float UpdateInterval = 10f;

    // Eris process_addictions ends at 50. The purging ritual's comment incorrectly says 40.
    [DataField]
    public int RecoveryThreshold = 50;

    /// <summary>Progress a dependence starts at once addiction triggers (Eris dependence = -15).</summary>
    [DataField]
    public int DependenceStart = -15;

    /// <summary>Per-tick chance a craving message/sanity hit fires while dependent.</summary>
    [DataField]
    public float CravingChance = 0.3f;

    /// <summary>Progress above which cravings start hurting sanity.</summary>
    [DataField]
    public int CravingSanityThreshold = 30;

    /// <summary>Progress above which the stronger sanity hit is used.</summary>
    [DataField]
    public int CravingSevereThreshold = 40;

    [DataField]
    public int CravingSanityDelta = -5;

    [DataField]
    public int CravingSevereSanityDelta = -10;

    /// <summary>Sanity restored when a dependence is fully recovered.</summary>
    [DataField]
    public int RecoverySanityDelta = 15;
}

[DataDefinition]
public sealed partial class ReagentDependence
{
    [DataField]
    public FixedPoint2 PeakDose;

    /// <summary>Null means exposure without dependence. A new dose sets dependence to -15.</summary>
    [DataField]
    public int? Progress;
}
