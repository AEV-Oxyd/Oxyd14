using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

[Prototype("oxydNeoTheologyRules")]
public sealed partial class NeoTheologyRulesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField]
    public bool Selected;

    [DataField]
    public double BaseHolinessPerMinute = 1d;

    [DataField]
    public double DiscipleCapacity = 50d;

    [DataField]
    public double PreacherCapacity = 80d;

    [DataField]
    public double InquisitorCapacity = 100d;

    [DataField]
    public double PreacherRegenMultiplier = 1.15d;

    [DataField]
    public double InquisitorRegenMultiplier = 1.25d;

    [DataField]
    public double DebitTolerance = 0.000001d;
}
