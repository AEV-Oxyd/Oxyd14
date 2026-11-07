using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Hardware;

[Serializable, NetSerializable]
public enum HardwarePartSize : byte
{
    Small = 1,
    Medium = 2,
    Large = 3
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerCpuComponent : Component
{
    [DataField] public float FrequencyMhz = 250f;
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerRamComponent : Component
{
    [DataField] public int SizeMb = 256;
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerStorageDeviceComponent : Component
{
    [DataField] public int CapacityBytes = 10485760; // 10 MB
    [DataField] public HardwarePartSize Size = HardwarePartSize.Medium;
    [DataField] public List<EntProtoId> PreloadedPrograms = new();
    [DataField] public List<string> StoredDesigns = new();
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerHardDriveComponent : Component
{
    [DataField] public int CapacityBytes = 10485760; // 10 MB
    [DataField] public HardwarePartSize Size = HardwarePartSize.Medium;
    [DataField] public List<EntProtoId> PreloadedPrograms = new();
    [DataField] public List<string> StoredDesigns = new();
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerNetworkCardComponent : Component
{
    [DataField] public int BandwidthBps = 50000; // 50 KB/s download
    [DataField] public int MaxUploadBps = 50000; // 50 KB/s upload
    [DataField] public bool Wireless = true;
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerBatteryModuleComponent : Component
{
    [DataField] public float MaxCharge = 1000f;
    [DataField] public float CurrentCharge = 1000f;
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerSensorModuleComponent : Component
{
    [DataField] public string SensorType = "Atmospheric";
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerIdCardSlotModuleComponent : Component
{
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerDiskSlotModuleComponent : Component
{
    [DataField] public HardwarePartSize Size = HardwarePartSize.Small;
    [DataField] public List<string> StoredDesigns = new();
}
