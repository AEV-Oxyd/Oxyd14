using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Pda;

public enum OxydHardwareSlot : byte
{
    Cpu,
    Hdd,
    Network,
    Peripheral, // tesla / printer / scanner / signaller
}

public enum OxydScannerKind : byte
{
    None = 0,
    Medical,
    Atmos,
    Price,
    Reagent,
}

/// <summary>Eris computer_hardware piece installable in a modular PDA bay.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydComputerHardwareComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public OxydHardwareSlot Slot;

    /// <summary>Maps to <see cref="Content.Shared.CartridgeLoader.CartridgeLoaderComponent.DiskSpace"/> when this is an HDD.</summary>
    [DataField, AutoNetworkedField]
    public int DiskCapacity;

    /// <summary>Bonus disk slots granted by advanced CPUs.</summary>
    [DataField, AutoNetworkedField]
    public int DiskBonus;

    /// <summary>Eris max simultaneous programs (status / future multitasking).</summary>
    [DataField, AutoNetworkedField]
    public int MaxPrograms = 1;

    /// <summary>Wireless network range override when this is a network card. 0 = leave default.</summary>
    [DataField, AutoNetworkedField]
    public int NetworkRange;

    [DataField, AutoNetworkedField]
    public bool LongRange;

    [DataField, AutoNetworkedField]
    public bool TeslaLink;

    [DataField, AutoNetworkedField]
    public bool Printer;

    /// <summary>Eris signal manager hardware — enables wireless DeviceLink signalling from the PDA.</summary>
    [DataField, AutoNetworkedField]
    public bool Signaller;

    /// <summary>Eris scanner module kind (medical / atmos / price / reagent).</summary>
    [DataField, AutoNetworkedField]
    public OxydScannerKind Scanner = OxydScannerKind.None;

    [DataField, AutoNetworkedField]
    public int PowerUsage;

    [DataField, AutoNetworkedField]
    public bool Critical;
}

/// <summary>Marks a PDA as supporting Eris-style swappable hardware bays.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydModularPdaComponent : Component
{
    public const string CpuSlotId = "PDA-hw-cpu";
    public const string HddSlotId = "PDA-hw-hdd";
    public const string NetSlotId = "PDA-hw-net";
    public const string PeripheralSlotId = "PDA-hw-peripheral";

    /// <summary>Fallback disk space when no HDD is installed.</summary>
    [DataField, AutoNetworkedField]
    public int BaseDiskSpace = 4;

    [DataField, AutoNetworkedField]
    public int DefaultNetworkRange = 500;

    [DataField]
    public EntProtoId? StartingCpu = "OxydHwCpuSmall";

    [DataField]
    public EntProtoId? StartingHdd = "OxydHwHddSmall";

    [DataField]
    public EntProtoId? StartingNetwork = "OxydHwNetBasic";

    [DataField]
    public EntProtoId? StartingPeripheral;

    /// <summary>Computed: effective max concurrent programs from CPU.</summary>
    [ViewVariables, AutoNetworkedField]
    public int EffectiveMaxPrograms = 2;

    [ViewVariables, AutoNetworkedField]
    public int EffectiveDiskSpace = 4;

    [ViewVariables, AutoNetworkedField]
    public int EffectiveNetworkRange = 500;

    [ViewVariables, AutoNetworkedField]
    public bool HasTeslaLink;

    [ViewVariables, AutoNetworkedField]
    public bool HasPrinter;

    [ViewVariables, AutoNetworkedField]
    public bool HasSignaller;

    [ViewVariables, AutoNetworkedField]
    public OxydScannerKind ActiveScanner = OxydScannerKind.None;

    /// <summary>Last price/reagent scan text for the scanner program.</summary>
    [ViewVariables, AutoNetworkedField]
    public string ScanBuffer = string.Empty;

    [ViewVariables, AutoNetworkedField]
    public int TotalPowerUsage;

    [DataField]
    public ItemSlot CpuSlot = new();

    [DataField]
    public ItemSlot HddSlot = new();

    [DataField]
    public ItemSlot NetSlot = new();

    [DataField]
    public ItemSlot PeripheralSlot = new();
}

[Serializable, NetSerializable]
public sealed class OxydHardwareReportMessage : BoundUserInterfaceMessage;
