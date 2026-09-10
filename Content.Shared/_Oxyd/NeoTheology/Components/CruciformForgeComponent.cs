using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.6: a NeoTheology forge. It banks materials handed to it (in its own
/// <see cref="MaterialStorageComponent"/>) and, once the recipe in <see cref="Needed"/> is stocked,
/// spends <see cref="WorkTime"/> turning them into a <see cref="Product"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformForgeComponent : Component
{
    /// <summary>
    /// Recipe, in SS14 material volume units. A standard sheet is 100 units (plasteel/gold),
    /// a biomatter sheet is 1, so this is Eris' "10 biomatter + 5 plasteel + 2 gold".
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Needed = new()
    {
        ["Biomatter"] = 10,
        ["Plasteel"] = 500,
        ["Gold"] = 200,
    };

    [DataField]
    public TimeSpan WorkTime = TimeSpan.FromSeconds(30);

    /// <summary>Mirrored by the prototype's <c>ApcPowerReceiver</c> load; the passive receiver does the draining.</summary>
    [DataField]
    public float PowerCost = 250f;

    [DataField]
    public EntProtoId Product = "OxydNtCruciform";

    [ViewVariables]
    public bool Working;

    [ViewVariables]
    public TimeSpan? StartedAt;

    [ViewVariables, AutoNetworkedField]
    public bool Ready;
}
