using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Medical;

/// <summary>
/// Carried by analgesic status-effect entities (see <c>OxydAnalgesic*</c> prototypes): the amount
/// of pain the effect suppresses while it is active on a mob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AnalgesicComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Strength;
}
