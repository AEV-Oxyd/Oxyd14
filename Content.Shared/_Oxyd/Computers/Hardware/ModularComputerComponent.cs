using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

[RegisterComponent, NetworkedComponent]
public sealed partial class ModularComputerComponent : Component
{
    public const string CpuSlotId = "computer_cpu";
    public const string RamSlotId = "computer_ram";
    public const string StorageSlotId = "computer_drive";
    public const string NetSlotId = "computer_nic";
    public const string BatterySlotId = "computer_battery";
    public const string SensorSlotId = "computer_sensor";
    public const string IdSlotModuleId = "computer_id_module";
    public const string DiskSlotModuleId = "computer_disk_module";
    public const string ExtraDriveContainerId = "computer_extra_drives";

    [DataField] public ItemSlot CpuSlot = new();
    [DataField] public ItemSlot RamSlot = new();
    [DataField] public ItemSlot StorageSlot = new();
    [DataField] public ItemSlot NetSlot = new();
    [DataField] public ItemSlot? BatterySlot;
    [DataField] public ItemSlot? SensorSlot;
    [DataField] public ItemSlot? IdSlotModule;
    [DataField] public ItemSlot? DiskSlotModule;

    [DataField] public int MaxHardDriveSlots = 2;
    [DataField] public int MaxRamSlots = 2;
    [DataField] public HardwarePartSize MaxHardDriveSize = HardwarePartSize.Large;
    [DataField] public HardwarePartSize MaxBatterySize = HardwarePartSize.Small;
}
