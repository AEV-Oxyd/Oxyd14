using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

[Prototype("oxydLitanySet")]
public sealed partial class LitanySetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public List<ProtoId<LitanyPrototype>> Litanies { get; private set; } = new();
}
