using Content.Shared.Access;
using Content.Shared.NPC.Prototypes;
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

    /// <summary>
    /// How far a channeling preacher's follower count reaches. Keeps the regeneration input a
    /// range lookup instead of a station-wide cruciform query.
    /// </summary>
    [DataField]
    public float ChannelingFollowerRange = 7f;

    /// <summary>Holiness capacity when a cruciform carries no configured profile.</summary>
    [DataField]
    public double DefaultCruciformCapacity = 50d;

    /// <summary>
    /// Access level → minimum clearance a bearer needs before the level is granted to their
    /// access holder. Levels absent from the map require <see cref="NeoTheologyClearance.None"/>.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<AccessLevelPrototype>, NeoTheologyClearance> RequiredClearance = new();

    /// <summary>Fauna the reveal-adversaries litany flags as hostile (the fork's simple-hostile set).</summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> HostileFactions = new()
    {
        "Dragon",
        "SimpleHostile",
        "Xeno",
    };
}
