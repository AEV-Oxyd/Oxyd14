using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Hardware;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ComputerStatsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float CpuFrequencyMhz = 100f;

    [DataField, AutoNetworkedField]
    public int RamMb = 64;

    [DataField, AutoNetworkedField]
    public int StorageBytes = 1048576; // 1 MB

    [DataField, AutoNetworkedField]
    public int NetworkBandwidthBps = 10000; // 10 KB/s

    /// <summary>
    /// If true, this device represents single-purpose embedded machinery (e.g. APCs, single-purpose vendor interfaces)
    /// and its raw file system and internals can only be inspected using a Chief Engineer Hardware Debugging Terminal.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool DirectHardwareAccessOnly = false;
}
