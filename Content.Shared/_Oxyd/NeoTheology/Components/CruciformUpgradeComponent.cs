using Content.Shared._Oxyd.NeoTheology;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// An installable cruciform attachment. Eris /obj/item/cruciform_upgrade/* reduced to data:
/// each upgrade is a flat delta applied while installed and reverted on removal.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CruciformUpgradeComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MaxHolinessDelta;

    [DataField, AutoNetworkedField]
    public float RegenMultiplierDelta;

    /// <summary>Extra damage-modifier the bearer gains while channelling (Eris faith's shield).</summary>
    [DataField, AutoNetworkedField]
    public float DamagedModifierDelta;

    /// <summary>Litany sets this attachment unlocks while installed.</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<LitanySetPrototype>> LitanySets = new();
}
