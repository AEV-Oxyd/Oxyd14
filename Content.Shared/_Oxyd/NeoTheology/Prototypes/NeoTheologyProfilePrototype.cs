using Content.Shared.Access;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Data-driven cruciform profile. A profile owns the rank-like values that used
/// to be spread across several enums and server-side switches.
/// </summary>
[Prototype("oxydNeoTheologyProfile")]
public sealed partial class NeoTheologyProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; private set; } = string.Empty;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> AccessPrivileges { get; private set; } = new();

    [DataField]
    public double CruciformCapacity { get; private set; }

    [DataField]
    public double RegenerationMultiplier { get; private set; } = 1d;

    [DataField]
    public List<ProtoId<LitanySetPrototype>> LitanySets { get; private set; } = new();

    /// <summary>
    /// Whether a bearer with this profile may use the channeling regeneration
    /// input. The actual channeling state remains a separate server-owned input.
    /// </summary>
    [DataField]
    public bool CanChannel { get; private set; }

    /// <summary>Whether this profile contributes to another channeler's follower count.</summary>
    [DataField]
    public bool CountsAsChannelingFollower { get; private set; }
}

