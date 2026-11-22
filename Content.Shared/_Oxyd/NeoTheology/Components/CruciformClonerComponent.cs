using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.12: marks a cloner as a NeoTheology one, and records the reader it takes its soul from.
/// Body growth itself is upstream <see cref="Content.Shared.Cloning.CloningPodComponent"/>'s job.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformClonerComponent : Component
{
    /// <summary>
    /// The reader whose soul this cloner grows a body for.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Reader;

    [DataField]
    public DamageSpecifier ResurrectionDamage = new() { DamageDict = { ["Cellular"] = 60 } };

    [DataField]
    public HashSet<ProtoId<NeoTheologyProfilePrototype>> DamageExemptProfiles = new()
    {
        "OxydNtInquisitor", "OxydNtPreacher", "OxydNtAcolyte",
    };
}
