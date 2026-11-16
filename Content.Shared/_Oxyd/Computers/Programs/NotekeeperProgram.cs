using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NotekeeperProgramComponent : Component
{
    [DataField, AutoNetworkedField]
    public string CurrentFilePath = "/notes/shift_notes.txt";
}

[Serializable, NetSerializable]
public sealed class NotekeeperUiState : BoundUserInterfaceState
{
    public bool HasHardDrive { get; }
    public string CurrentFilePath { get; }
    public string CurrentFileName { get; }
    public string FileContent { get; }
    public List<string> AvailableFiles { get; }
    public bool IsReadOnly { get; }

    public NotekeeperUiState(
        bool hasHardDrive,
        string currentFilePath,
        string currentFileName,
        string fileContent,
        List<string> availableFiles,
        bool isReadOnly)
    {
        HasHardDrive = hasHardDrive;
        CurrentFilePath = currentFilePath;
        CurrentFileName = currentFileName;
        FileContent = fileContent;
        AvailableFiles = availableFiles;
        IsReadOnly = isReadOnly;
    }
}

[Serializable, NetSerializable]
public enum NotekeeperUiAction : byte
{
    OpenFile,
    SaveFile,
    CreateFile,
    DeleteFile,
    LiveDraftUpdate
}

[Serializable, NetSerializable]
public sealed class NotekeeperActionMessage : BoundUserInterfaceMessage
{
    public NotekeeperUiAction Action { get; }
    public string Path { get; }
    public string Content { get; }

    public NotekeeperActionMessage(NotekeeperUiAction action, string path = "", string content = "")
    {
        Action = action;
        Path = path;
        Content = content;
    }
}
