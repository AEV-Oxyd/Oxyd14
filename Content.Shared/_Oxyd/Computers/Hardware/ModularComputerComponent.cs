using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

/// <summary>
/// Defines modular physical hardware slots and expansion capacities on a computer motherboard.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModularComputerComponent : Component
{
    public const string ContainerHardware = "hardware_container";
    public const string SlotCpu = "cpu_slot";
    public const string SlotRam = "ram_slot";
    public const string SlotRamSecondary = "ram_slot_2";
    public const string SlotStorage = "storage_slot";
    public const string SlotNetwork = "net_slot";
    public const string SlotBattery = "battery_slot";
    public const string SlotSensor = "sensor_slot";
    public const string SlotIdModule = "id_slot_module";
    public const string SlotIdModuleSecondary = "id_slot_module_2";
    public const string SlotDiskModule = "disk_slot_module";

    [DataField, AutoNetworkedField]
    public int MaxHardDriveSlots = 1;

    [DataField, AutoNetworkedField]
    public int MaxRamSlots = 2;

    [DataField, AutoNetworkedField]
    public int MaxIdModuleSlots = 2;

    [DataField, AutoNetworkedField]
    public HardwarePartSize MaxHardDriveSize = HardwarePartSize.Medium;

    [DataField, AutoNetworkedField]
    public HardwarePartSize MaxBatterySize = HardwarePartSize.Small;

    [DataField, AutoNetworkedField]
    public List<EntityUid> InstalledParts = new();
}
