using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared.Damage;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// The authoritative persistent state carried by an actual NeoTheology implant.
/// Runtime ownership is established by <see cref="CruciformBearerComponent"/>;
/// adding this component to a body is not sufficient to make it a bearer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? ImplantedEntity;

    /// <summary>
    /// Records prior activation. Death and extraction clear Active, but do not make an implant unused.
    /// Revival can restore activation; reimplantation waits for the saved-soul rite.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool EverActivated;

    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public double Holiness;

    [ViewVariables, AutoNetworkedField]
    public double MaxHoliness;

    [ViewVariables, AutoNetworkedField]
    public double RegenerationPerSecond;

    /// <summary>
    /// Temporary regeneration multiplier contributed by auras (the obelisk's faithful buff). It is
    /// an input to <see cref="CruciformSystem.RecomputeProfile"/> alongside profile ∪ modules ∪
    /// upgrade, so repeated aura ticks can never compound and an out-of-aura reset is exact.
    /// </summary>
    [ViewVariables]
    public double RegenerationMultiplier = 1.0;

    /// <summary>Active obelisk contributions. Only the strongest multiplier applies.</summary>
    public Dictionary<EntityUid, float> ObeliskRegeneration = new();

    [DataField, AutoNetworkedField]
    public ProtoId<NeoTheologyProfilePrototype> Profile = "OxydNtDisciple";

    /// <summary>Eris <c>security_clearance</c>. Rank modules do not change this.</summary>
    [DataField, AutoNetworkedField]
    public NeoTheologyClearance Clearance = NeoTheologyClearance.None;

    /// <summary>
    /// Source-derived profile inputs. They remain separate from the configured
    /// profile so role/module systems can update them without silently changing
    /// profile authority.
    /// </summary>
    [DataField]
    public float RighteousLife;

    [DataField]
    public bool Channeling;

    /// <summary>
    /// Installed core modules. Litany sets, access and the two stat multipliers are
    /// derived from installed modules — profile starting modules are a grant recipe, not a second entitlement.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<CoreModulePrototype>> InstalledModules = new();

    [DataField]
    public HashSet<ProtoId<LitanySetPrototype>> UnlockedSets = new();

    /// <summary>
    /// Sets a litany granted at runtime (the Crusade rite; Eris <c>known_rituals |=</c>).
    /// <see cref="Content.Server._Oxyd.NeoTheology.CruciformSystem.RecomputeProfile"/> re-derives
    /// <see cref="UnlockedSets"/> from installed modules and upgrade, so a grant that only wrote
    /// there would vanish on the next recompute. Grants live here instead.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<LitanySetPrototype>> GrantedSets = new();

    /// <summary>
    /// Installed cruciform attachment, if any. Its deltas ride the same derivation as
    /// profile ∪ modules inside <c>RecomputeProfile</c>.
    /// </summary>
    [DataField]
    public EntityUid? Upgrade;

    /// <summary>Installed core upgrade items keyed by the module they supply. Asacris removes these only.</summary>
    [DataField]
    public Dictionary<ProtoId<CoreModulePrototype>, EntityUid> CoreUpgrades = new();

    /// <summary>
    /// Simulation timestamp used to settle regeneration. It is re-anchored whenever
    /// activation or implantation state changes so detached time is never retroactive.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastHolinessUpdate;

    [DataField] public TimeSpan PurityInterval = TimeSpan.FromSeconds(5);
    public TimeSpan NextPurity;

    /// <summary>Next time the channeling follower roster (and thus regeneration) is recounted.</summary>
    [DataField] public TimeSpan RosterCheckInterval = TimeSpan.FromSeconds(1);
    public TimeSpan NextRosterCheck;

    /// <summary>Each Eye energy miracle adds one source base regeneration, not current holiness.</summary>
    [DataField] public int EnergyMiracles;

    /// <summary>
    /// Healing applied per pulse to this bearer standing in a Holy Light aura, scaled by the
    /// light's <c>Healing</c> intensity. Negative damage heals.
    /// </summary>
    [DataField]
    public DamageSpecifier HolyLightHealing = new()
    {
        DamageDict = { ["Blunt"] = -1f / 3f, ["Slash"] = -1f / 3f, ["Piercing"] = -1f / 3f, ["Heat"] = -1f },
    };
}
