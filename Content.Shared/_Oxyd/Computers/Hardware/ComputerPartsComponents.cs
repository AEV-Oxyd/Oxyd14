using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerCpuComponent : Component
{
    [DataField] public float FrequencyMhz = 250f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerRamComponent : Component
{
    [DataField] public int SizeMb = 256;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerStorageDeviceComponent : Component
{
    [DataField] public int CapacityBytes = 10485760; // 10 MB
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ComputerNetworkCardComponent : Component
{
    [DataField] public int BandwidthBps = 50000; // 50 KB/s
    [DataField] public bool Wireless = true;
}
