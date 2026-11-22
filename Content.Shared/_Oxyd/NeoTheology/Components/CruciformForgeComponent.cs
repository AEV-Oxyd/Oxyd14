using Content.Shared.Research.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.6: a NeoTheology forge. Production itself rides <see cref="LatheComponent"/> — the forge
/// banks materials in its <see cref="MaterialStorageComponent"/>, marks the machine as a litany
/// target and names the <see cref="Recipe"/> the litany queues.
/// </summary>
[RegisterComponent]
public sealed partial class CruciformForgeComponent : Component
{
    /// <summary>
    /// The stock <see cref="LatheRecipePrototype"/> the forge produces — materials, result and
    /// work time all come from the recipe, not a bespoke dictionary.
    /// </summary>
    [DataField]
    public ProtoId<LatheRecipePrototype> Recipe = "OxydNtCruciform";
}
