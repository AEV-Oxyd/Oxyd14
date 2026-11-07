using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.FileSystem;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class FileSystemComponent : Component
{
    [DataField]
    public Dictionary<string, VirtualFile> Files = new();

    [AutoNetworkedField, ViewVariables]
    public List<FileHeader> NetworkedHeaders = new();

    [DataField]
    public int MaxStorageBytes = 1048576; // 1 MB default

    [ViewVariables]
    public int UsedStorageBytes = 0;
}
