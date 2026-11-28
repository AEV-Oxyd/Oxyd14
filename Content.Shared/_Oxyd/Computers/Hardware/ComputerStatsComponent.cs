using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

/// <summary>
/// Cached effective hardware capabilities derived exclusively from the physical
/// hardware item entities installed in the computer's slots/container.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerStatsComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool HasCpu;

    [DataField, AutoNetworkedField]
    public int CpuFrequencyMhz;

    [DataField, AutoNetworkedField]
    public int MaxConcurrentPrograms;

    [DataField, AutoNetworkedField]
    public float CpuCapacity;

    [DataField, AutoNetworkedField]
    public float UsedCpuUnits;

    [DataField, AutoNetworkedField]
    public bool HasRam;

    [DataField, AutoNetworkedField]
    public int RamMb;

    [DataField, AutoNetworkedField]
    public int UsedRamMb;

    [DataField, AutoNetworkedField]
    public bool HasHardDrive;

    [DataField, AutoNetworkedField]
    public EntityUid? PrimaryDriveUid;

    [DataField, AutoNetworkedField]
    public int StorageBytes;

    [DataField, AutoNetworkedField]
    public int UsedStorageBytes;

    [DataField, AutoNetworkedField]
    public bool HasNetworkCard;

    [DataField, AutoNetworkedField]
    public int NetworkBandwidthBps;

    [DataField, AutoNetworkedField]
    public int NetworkUploadBandwidthBps;

    [DataField, AutoNetworkedField]
    public bool HasBattery;

    [DataField, AutoNetworkedField]
    public float BatteryCharge;

    [DataField, AutoNetworkedField]
    public float BatteryMaxCharge;

    [DataField, AutoNetworkedField]
    public bool HasDataPort;

    [DataField, AutoNetworkedField]
    public bool HasSensorModule;

    [DataField, AutoNetworkedField]
    public bool HasHealthScannerModule;

    [DataField, AutoNetworkedField]
    public bool HasPrinterModule;

    [DataField, AutoNetworkedField]
    public bool HasIdSlotModule;

    [DataField, AutoNetworkedField]
    public bool HasDiskSlotModule;

    [DataField, AutoNetworkedField]
    public float CurrentCpuLoadPercent;

    [DataField, AutoNetworkedField]
    public bool IsBooted;

    [DataField, AutoNetworkedField]
    public string BootError = string.Empty;
}
