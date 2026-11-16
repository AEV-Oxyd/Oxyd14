using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Computers.Programs;

/// <summary>
/// Core component attached to every instantiated program entity inside a computer's
/// program_container. Each program entity carries its own UserInterfaceComponent and
/// communicates directly with clients using BoundUserInterface states.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ProgramComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Computer;

    [DataField, AutoNetworkedField]
    public string Title = "Program";

    [DataField, AutoNetworkedField]
    public EntProtoId? Prototype;

    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Icon;

    [DataField, AutoNetworkedField]
    public int RamUsageMb = 8;

    [DataField, AutoNetworkedField]
    public float CpuUsagePercent = 4.0f;

    [DataField, AutoNetworkedField]
    public bool IsBootProgram;

    [DataField, AutoNetworkedField]
    public string ExecutablePath = string.Empty;
}
