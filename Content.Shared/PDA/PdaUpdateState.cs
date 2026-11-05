using Content.Shared.CartridgeLoader;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public sealed class PdaUpdateState : CartridgeLoaderUiState, IBuiNestedState // WTF is this. what. I ... fuck me I just want net entities to work
        // TODO purge this shit
        //AAAAAAAAAAAAAAAA
    {
        public bool FlashlightEnabled;
        public bool HasPen;
        public bool HasPai;
        public bool HasDisk;
        public string? DiskName;
        public PdaIdInfoText PdaOwnerInfo;
        public string? StationName;
        public bool HasUplink;
        public bool CanPlayMusic;
        public string? Address;
        public string? DeviceGuid;
        public float? CpuUsagePercent;
        public int? RamUsedMb;
        public int? RamTotalMb;
        public int? StorageUsedBytes;
        public int? StorageTotalBytes;
        public bool IsNetworkSaturated;
        public List<NetEntity> OpenPrograms;
        public BoundUserInterfaceState? CartridgeState;
        public bool HasCaptainAccess;
        public string? SshPassword;
        public int CurrentView;

        public IEnumerable<BoundUserInterfaceState> GetNestedStates()
        {
            if (CartridgeState != null)
                yield return CartridgeState;
        }

        public PdaUpdateState(
            List<NetEntity> programs,
            NetEntity? activeUI,
            bool flashlightEnabled,
            bool hasPen,
            bool hasPai,
            PdaIdInfoText pdaOwnerInfo,
            string? stationName,
            bool hasUplink = false,
            bool canPlayMusic = false,
            string? address = null,
            string? deviceGuid = null,
            float? cpuUsagePercent = null,
            int? ramUsedMb = null,
            int? ramTotalMb = null,
            int? storageUsedBytes = null,
            int? storageTotalBytes = null,
            bool isNetworkSaturated = false,
            List<NetEntity>? openPrograms = null,
            BoundUserInterfaceState? cartridgeState = null,
            bool hasDisk = false,
            string? diskName = null,
            bool hasCaptainAccess = false,
            string? sshPassword = null,
            int currentView = 0)
            : base(programs, activeUI)
        {
            FlashlightEnabled = flashlightEnabled;
            HasPen = hasPen;
            HasPai = hasPai;
            HasDisk = hasDisk;
            DiskName = diskName;
            PdaOwnerInfo = pdaOwnerInfo;
            HasUplink = hasUplink;
            CanPlayMusic = canPlayMusic;
            StationName = stationName;
            Address = address;
            DeviceGuid = deviceGuid;
            CpuUsagePercent = cpuUsagePercent;
            RamUsedMb = ramUsedMb;
            RamTotalMb = ramTotalMb;
            StorageUsedBytes = storageUsedBytes;
            StorageTotalBytes = storageTotalBytes;
            IsNetworkSaturated = isNetworkSaturated;
            OpenPrograms = openPrograms ?? new();
            CartridgeState = cartridgeState;
            HasCaptainAccess = hasCaptainAccess;
            SshPassword = sshPassword;
            CurrentView = currentView;
        }
    }

    [Serializable, NetSerializable]
    public struct PdaIdInfoText
    {
        public string? ActualOwnerName;
        public string? IdOwner;
        public string? JobTitle;
        public string? StationAlertLevel;
        public Color StationAlertColor;
    }
}
