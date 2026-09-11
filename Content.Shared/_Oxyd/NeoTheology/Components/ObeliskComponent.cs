using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.13: the NeoTheology obelisk's aura (Eris <c>machinery/obelisk.dm</c>). Faithful bearers in
/// range get a sanity perk and multiplied regeneration, simple mobs in range take damage up to
/// <see cref="MaxTargets"/>, and botany trays in range lose their weeds.
/// The plan spells the last attribute <c>AutoNetworkedField</c>, which is a field attribute — the
/// component-level one is <c>AutoGenerateComponentState</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ObeliskComponent : Component
{
    /// <summary>Supported hostile fauna. Faction membership alone does not identify a mob as fauna.</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> HostileFactions = new() { "Carp", "SimpleHostile", "Xeno" };

    [ViewVariables]
    public TimeSpan NextPulse;

    [DataField]
    public float Radius = 7f;

    [DataField]
    public float HostileDamage = 45f;

    [DataField]
    public int MaxTargets = 7;

    /// <summary>Regeneration multiplier applied to faithful in range.</summary>
    [DataField]
    public float RegenMultiplier = 2f;

    [DataField]
    public float ObservationPerFaithful = 20f;

    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1.5);
}
