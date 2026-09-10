using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.6: a NeoTheology forge. It banks materials handed to it and, once the recipe in
/// <see cref="Needed"/> is stocked, spends <see cref="WorkTime"/> turning them into a
/// <see cref="Product"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformForgeComponent : Component
{
    /// <summary>Recipe. Amounts are in sheets, not SS14 material units (Biomatter is not an SS14 material).</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Needed = new()
    {
        ["Biomatter"] = 10,
        ["Plasteel"] = 5,
        ["Gold"] = 2,
    };

    /// <summary>Per-material storage ceiling.</summary>
    [DataField]
    public int StorageCapacity = 50;

    [DataField]
    public TimeSpan WorkTime = TimeSpan.FromSeconds(30);

    /// <summary>Mirrored by the prototype's <c>ApcPowerReceiver</c> load; the passive receiver does the draining.</summary>
    [DataField]
    public float PowerCost = 250f;

    [DataField]
    public EntProtoId Product = "OxydNtCruciform";

    /// <summary>Banked material, in sheets.</summary>
    [ViewVariables]
    public Dictionary<ProtoId<MaterialPrototype>, int> Stored = new();

    [ViewVariables]
    public bool Working;

    [ViewVariables]
    public TimeSpan? StartedAt;

    [ViewVariables, AutoNetworkedField]
    public bool Ready;
}
