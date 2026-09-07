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
    public double MaxHoliness = 50d;

    [ViewVariables, AutoNetworkedField]
    public double RegenerationPerSecond;

    [DataField, AutoNetworkedField]
    public NeoTheologyRank Rank = NeoTheologyRank.Disciple;

    [DataField, AutoNetworkedField]
    public NeoTheologySpecialization Specialization = NeoTheologySpecialization.None;

    [DataField, AutoNetworkedField]
    public NeoTheologyClearance Clearance = NeoTheologyClearance.None;

    /// <summary>
    /// Source-derived profile inputs. They are kept separate from rank so a future
    /// role/module system can update them without silently changing authority.
    /// </summary>
    [DataField]
    public float RighteousLife;

    [DataField]
    public bool Channeling;

    [DataField]
    public HashSet<ProtoId<LitanySetPrototype>> UnlockedSets = new();

    /// <summary>
    /// Simulation timestamp used to settle regeneration. It is re-anchored whenever
    /// activation or implantation state changes so detached time is never retroactive.
    /// </summary>
    [DataField]
    public TimeSpan LastHolinessUpdate;

    /// <summary>
    /// A one-shot module marker used by the foundation scenario and future core-module
    /// integration. It is deliberately not treated as a rank grant by itself.
    /// </summary>
    [DataField]
    public bool PreacherAscensionKitInstalled;

    [DataField]
    public bool PreacherAscensionKitActive;
}
