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

    [DataField] public ItemSlot CpuSlot = new();
    [DataField] public ItemSlot RamSlot = new();
    [DataField] public ItemSlot StorageSlot = new();
    [DataField] public ItemSlot NetSlot = new();
}
