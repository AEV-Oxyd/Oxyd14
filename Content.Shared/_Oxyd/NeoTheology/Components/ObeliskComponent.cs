using Content.Shared.Damage;
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
    /// <summary>Supported hostile fauna. Faction membership alone does not identify a mob as fauna.
    /// Carp are in the <c>Dragon</c> faction (Resources/Prototypes/Entities/Mobs/NPCs/carp.yml).</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> HostileFactions = new() { "Dragon", "SimpleHostile", "Xeno" };

    [DataField]
    public float Radius = 7f;

    /// <summary>Damage applied to every hostile in range per aura pulse.</summary>
    [DataField]
    public DamageSpecifier HostileDamage = new()
    {
        DamageDict = { ["Blunt"] = 30f },
    };

    [DataField]
    public int MaxTargets = 7;

    /// <summary>Regeneration multiplier applied to faithful in range.</summary>
    [DataField]
    public float RegenMultiplier = 2f;

    [DataField]
    public float ObservationPerFaithful = 20f;

    /// <summary>Sanity restored to each faithful in range per aura pulse.</summary>
    [DataField]
    public float SanityPerSecond = 2f / 3f;

    /// <summary>Weed level removed from every tray in range per pulse. Larger than every tray max.</summary>
    [DataField]
    public float WeedRemovalPerSecond = 200f / 3f;

    [DataField, AutoNetworkedField]
    public bool Active;

    /// <summary>
    /// Sanctify forces the obelisk on until this time (Eris <c>force_active = max(60, ...)</c>).
    /// <see cref="Active"/> stays the computed state; the tick ORs this deadline in.
    /// </summary>
    public TimeSpan ForceActiveUntil;
}
