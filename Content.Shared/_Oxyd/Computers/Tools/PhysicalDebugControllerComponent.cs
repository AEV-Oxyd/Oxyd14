using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Tools;

/// <summary>
/// Handheld engineering physical diagnostic terminal controller.
/// Clicking any computer or <see cref="EmbeddedComputerHostComponent"/> physically links this
/// device's Terminal directly to the target without requiring a Network Card or Station Network Router.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PhysicalDebugControllerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MaxPhysicalRange = 3.5f;

    [DataField, AutoNetworkedField]
    public EntityUid? LinkedTargetComputer;
}
