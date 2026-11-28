using Content.Shared.Access;
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

    [DataField]
    public int FileSizeBytes = 65536;

    [DataField, AutoNetworkedField]
    public float CpuUsagePercent = 4.0f;

    /// <summary>
    /// Additional dynamic CPU usage units added at runtime by active workloads
    /// (e.g. live camera feeds, active file transfers, lathe printing).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DynamicCpuUsageBonus;

    [DataField, AutoNetworkedField]
    public bool IsBootProgram;

    /// <summary>
    /// Marks this program prototype as an Operating System kernel/shell.
    /// Operating systems boot as the computer's primary BootProgram and cannot be launched
    /// as nested child apps inside another running operating system.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsOperatingSystem;

    /// <summary>
    /// Marks a jailbroken / modified operating system that completely disables remote control
    /// (including corporate SystemAdmin remote management) and allows unrestricted local security tools.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsJailbrokenOs;

    /// <summary>
    /// Access levels permitted to download this program from a station Data Server repository.
    /// Empty means publicly downloadable; non-empty requires ANY listed access (or SystemAdmin).
    /// </summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> RequiredDownloadAccess = new();

    [DataField, AutoNetworkedField]
    public string ExecutablePath = string.Empty;

    [DataField, AutoNetworkedField]
    public string? ScreenState;
}
