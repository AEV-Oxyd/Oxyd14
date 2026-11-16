using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

public enum HardwarePartSize : byte
{
    Small = 0,
    Medium = 1,
    Large = 2
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerCpuComponent : Component
{
    [DataField, AutoNetworkedField]
    public int FrequencyMhz = 800;

    [DataField, AutoNetworkedField]
    public int MaxConcurrentPrograms = 4;

    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerRamComponent : Component
{
    [DataField, AutoNetworkedField]
    public int SizeMb = 512;

    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerHardDriveComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CapacityBytes = 8388608;

    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Medium;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerNetworkCardComponent : Component
{
    [DataField, AutoNetworkedField]
    public int BandwidthBps = 500000;

    [DataField, AutoNetworkedField]
    public int MaxUploadBps = 250000;

    [DataField, AutoNetworkedField]
    public int BufferQueueLimit = 50;

    [DataField, AutoNetworkedField]
    public bool SupportsWired = true;

    [DataField, AutoNetworkedField]
    public bool SupportsWireless = true;

    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerBatteryModuleComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MaxCharge = 1000f;

    [DataField, AutoNetworkedField]
    public float CurrentCharge = 1000f;

    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerSensorModuleComponent : Component
{
    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerIdCardSlotModuleComponent : Component
{
    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerDiskSlotModuleComponent : Component
{
    [DataField, AutoNetworkedField]
    public HardwarePartSize Size = HardwarePartSize.Small;
}

/// <summary>
/// Attached to computer entities equipped with a physical data port jack,
/// allowing local terminal connection via diagnostic tools (such as the Hardware Debugging Terminal).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerDataPortComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public string PortLabel = "JTAG-0";

    [DataField, AutoNetworkedField]
    public bool RequireMaintenancePanelOpen;
}
