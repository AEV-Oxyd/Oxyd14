using Content.Shared._Oxyd.Computers.FileSystem;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Cartridges;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DownloaderCartridgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public string SelectedServerGuid = string.Empty;

    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;

    [DataField, AutoNetworkedField]
    public string CurrentCategory = "All";

    [DataField, AutoNetworkedField]
    public string? ActiveDownloadItemId;

    [DataField, AutoNetworkedField]
    public float DownloadRemainingSeconds;

    [DataField, AutoNetworkedField]
    public float DownloadTotalSeconds;

    [DataField]
    public VirtualFile? PendingFile;

    [DataField]
    public string? PendingItemName;

    [DataField]
    public string? PendingProgramProtoId;

    [DataField]
    public string? PendingProgramName;

    [DataField]
    public int PendingProgramSize;
}
