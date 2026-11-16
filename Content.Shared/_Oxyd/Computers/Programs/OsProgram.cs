using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OsProgramComponent : Component
{
    /// <summary>
    /// Persistent desktop view preference: 0 = Home, 1 = Programs, 2 = Hardware & Links, 3 = Active App.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentView;
}

[Serializable, NetSerializable]
public sealed class OsProgramEntryDto
{
    public NetEntity? RunningEntity { get; }
    public string FilePath { get; }
    public string Name { get; }
    public string PrototypeId { get; }
    public SpriteSpecifier? Icon { get; }
    public int RamUsageMb { get; }
    public float CpuUsagePercent { get; }
    public bool IsRunning { get; }
    public bool IsBootProgram { get; }

    public OsProgramEntryDto(
        NetEntity? runningEntity,
        string filePath,
        string name,
        string prototypeId,
        SpriteSpecifier? icon,
        int ramUsageMb,
        float cpuUsagePercent,
        bool isRunning,
        bool isBootProgram)
    {
        RunningEntity = runningEntity;
        FilePath = filePath;
        Name = name;
        PrototypeId = prototypeId;
        Icon = icon;
        RamUsageMb = ramUsageMb;
        CpuUsagePercent = cpuUsagePercent;
        IsRunning = isRunning;
        IsBootProgram = isBootProgram;
    }
}

[Serializable, NetSerializable]
public sealed class OsHardwareSummaryDto
{
    public int CpuMhz { get; }
    public float CpuLoadPercent { get; }
    public int MaxPrograms { get; }
    public int RamUsedMb { get; }
    public int RamTotalMb { get; }
    public int StorageUsedBytes { get; }
    public int StorageTotalBytes { get; }
    public int BandwidthBps { get; }
    public int UploadBandwidthBps { get; }
    public bool HasNetworkCard { get; }
    public bool HasDataPort { get; }
    public bool HasDiskSlot { get; }
    public bool HasIdSlot { get; }
    public bool IsPowered { get; }
    public bool AllowRemoteControl { get; }
    public bool AllowDeviceLink { get; }
    public string DeviceGuid { get; }
    public string HostName { get; }
    public string StatusMessage { get; }

    public OsHardwareSummaryDto(
        int cpuMhz,
        float cpuLoadPercent,
        int maxPrograms,
        int ramUsedMb,
        int ramTotalMb,
        int storageUsedBytes,
        int storageTotalBytes,
        int bandwidthBps,
        int uploadBandwidthBps,
        bool hasNetworkCard,
        bool hasDataPort,
        bool hasDiskSlot,
        bool hasIdSlot,
        bool isPowered,
        bool allowRemoteControl,
        bool allowDeviceLink,
        string deviceGuid,
        string hostName,
        string statusMessage)
    {
        CpuMhz = cpuMhz;
        CpuLoadPercent = cpuLoadPercent;
        MaxPrograms = maxPrograms;
        RamUsedMb = ramUsedMb;
        RamTotalMb = ramTotalMb;
        StorageUsedBytes = storageUsedBytes;
        StorageTotalBytes = storageTotalBytes;
        BandwidthBps = bandwidthBps;
        UploadBandwidthBps = uploadBandwidthBps;
        HasNetworkCard = hasNetworkCard;
        HasDataPort = hasDataPort;
        HasDiskSlot = hasDiskSlot;
        HasIdSlot = hasIdSlot;
        IsPowered = isPowered;
        AllowRemoteControl = allowRemoteControl;
        AllowDeviceLink = allowDeviceLink;
        DeviceGuid = deviceGuid;
        HostName = hostName;
        StatusMessage = statusMessage;
    }
}

[Serializable, NetSerializable]
public sealed class OsUiState : BoundUserInterfaceState
{
    public string HostTitle { get; }
    public string HostSubtitle { get; }
    public string BorderColor { get; }
    public string? OwnerName { get; }
    public string? OwnerJob { get; }
    public string? InsertedDiskName { get; }
    public bool FlashlightOn { get; }
    public int CurrentView { get; }
    public NetEntity? ActiveChildProgram { get; }
    public List<OsProgramEntryDto> InstalledExecutables { get; }
    public List<OsProgramEntryDto> RunningPrograms { get; }
    public List<MirroredDevicePortInfo> DevicePorts { get; }
    public OsHardwareSummaryDto Hardware { get; }

    public OsUiState(
        string hostTitle,
        string hostSubtitle,
        string borderColor,
        string? ownerName,
        string? ownerJob,
        string? insertedDiskName,
        bool flashlightOn,
        int currentView,
        NetEntity? activeChildProgram,
        List<OsProgramEntryDto> installedExecutables,
        List<OsProgramEntryDto> runningPrograms,
        List<MirroredDevicePortInfo> devicePorts,
        OsHardwareSummaryDto hardware)
    {
        HostTitle = hostTitle;
        HostSubtitle = hostSubtitle;
        BorderColor = borderColor;
        OwnerName = ownerName;
        OwnerJob = ownerJob;
        InsertedDiskName = insertedDiskName;
        FlashlightOn = flashlightOn;
        CurrentView = currentView;
        ActiveChildProgram = activeChildProgram;
        InstalledExecutables = installedExecutables;
        RunningPrograms = runningPrograms;
        DevicePorts = devicePorts;
        Hardware = hardware;
    }
}

[Serializable, NetSerializable]
public sealed class OsSetViewMessage : BoundUserInterfaceMessage
{
    public int ViewIndex { get; }

    public OsSetViewMessage(int viewIndex)
    {
        ViewIndex = viewIndex;
    }
}

[Serializable, NetSerializable]
public sealed class OsLaunchProgramMessage : BoundUserInterfaceMessage
{
    public string ExecutablePath { get; }

    public OsLaunchProgramMessage(string executablePath)
    {
        ExecutablePath = executablePath;
    }
}

[Serializable, NetSerializable]
public sealed class OsSwitchProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public OsSwitchProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class OsCloseProgramMessage : BoundUserInterfaceMessage
{
    public NetEntity ProgramEntity { get; }

    public OsCloseProgramMessage(NetEntity programEntity)
    {
        ProgramEntity = programEntity;
    }
}

[Serializable, NetSerializable]
public sealed class OsHardwareActionMessage : BoundUserInterfaceMessage
{
    public string Action { get; }
    public string Argument { get; }

    public OsHardwareActionMessage(string action, string argument = "")
    {
        Action = action;
        Argument = argument;
    }
}

[Serializable, NetSerializable]
public sealed class OsInvokeDevicePortMessage : BoundUserInterfaceMessage
{
    public string PortId { get; }
    public bool IsHostPort { get; }
    public string Command { get; }
    public string Argument { get; }

    public OsInvokeDevicePortMessage(string portId, bool isHostPort, string command = "", string argument = "")
    {
        PortId = portId;
        IsHostPort = isHostPort;
        Command = command;
        Argument = argument;
    }
}
