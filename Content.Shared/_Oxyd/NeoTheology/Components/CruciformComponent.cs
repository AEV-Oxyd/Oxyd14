using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

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

    [DataField, AutoNetworkedField]
    public ProtoId<NeoTheologyProfilePrototype> Profile = "OxydNtDisciple";

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
    /// derived from profile ∪ modules — never written directly by feature code.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<CoreModulePrototype>> InstalledModules = new();

    [DataField]
    public HashSet<ProtoId<LitanySetPrototype>> UnlockedSets = new();

    /// <summary>
    /// Installed cruciform attachment, if any. Its deltas ride the same derivation as
    /// profile ∪ modules inside <c>RecomputeProfile</c>.
    /// </summary>
    [DataField]
    public EntityUid? Upgrade;

    /// <summary>
    /// Simulation timestamp used to settle regeneration. It is re-anchored whenever
    /// activation or implantation state changes so detached time is never retroactive.
    /// </summary>
    [DataField]
    public TimeSpan LastHolinessUpdate;
}
