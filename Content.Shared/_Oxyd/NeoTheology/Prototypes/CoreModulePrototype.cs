using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Eris <c>/datum/core_module</c> ported as data. A module bundles the litany sets,
/// access levels and cruciform stat deltas that an install grants. Behaviour that is
/// genuinely special (cloning snapshot, obey laws, HUD, uplink) subscribes to
/// <see cref="CoreModuleInstalledEvent"/> from its own system instead of overriding here.
/// </summary>
[Prototype("oxydCoreModule")]
public sealed partial class CoreModulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>Eris <c>ritual_types</c>: sets unlocked while installed.</summary>
    [DataField]
    public List<ProtoId<LitanySetPrototype>> LitanySets { get; private set; } = new();

    /// <summary>Eris <c>access</c>: access levels granted to the bearer's id.</summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> Access { get; private set; } = new();

    /// <summary>Multiplied into profile capacity. Eris red_light = 1.6, inquisitor = 2.0.</summary>
    [DataField]
    public float MaxHolinessMultiplier { get; private set; } = 1f;

    /// <summary>Added to the profile regeneration multiplier. Eris red_light = 0.15, inquisitor = 0.25.</summary>
    [DataField]
    public float RegenMultiplierDelta { get; private set; }

    /// <summary>
    /// Set on "activatable" Eris modules: activating the cruciform while this module is
    /// installed converts the bearer to this profile (priest_convert → Preacher).
    /// </summary>
    [DataField]
    public ProtoId<NeoTheologyProfilePrototype>? ActivationProfile { get; private set; }
}
