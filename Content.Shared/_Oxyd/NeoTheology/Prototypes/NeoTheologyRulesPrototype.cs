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

    [DataField(required: true)]
    public List<ProtoId<NeoTheologyProfilePrototype>> Profiles { get; private set; } = new();

    [DataField]
    public double DebitTolerance = 0.000001d;
}
