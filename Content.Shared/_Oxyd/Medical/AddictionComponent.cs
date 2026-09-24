using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>Records exposure and dependence separately from the blood's current reagent contents.</summary>
[RegisterComponent]
public sealed partial class AddictionComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, ReagentDependence> Reagents = new();

    [DataField]
    public float UpdateRemaining = 10f;

    [DataField]
    public float UpdateInterval = 10f;

    // Eris process_addictions ends at 50. The purging ritual's comment incorrectly says 40.
    [DataField]
    public int RecoveryThreshold = 50;
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
