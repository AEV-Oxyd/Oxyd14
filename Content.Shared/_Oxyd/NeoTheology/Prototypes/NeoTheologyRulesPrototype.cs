using Content.Shared.Roles;
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

    /// <summary>
    /// Job id → cruciform profile. A player spawning into a mapped job receives an active
    /// cruciform carrying that profile and its rank modules. Empty by default, so nothing
    /// changes for stations that never set it.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, ProtoId<NeoTheologyProfilePrototype>> JobProfiles { get; private set; } = new();
}
