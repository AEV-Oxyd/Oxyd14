using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class NotekeeperUiState : BoundUserInterfaceState
{
    public List<string> Notes;
    public string CurrentFilePath;
    public string CurrentFileName;
    public string FileContent;
    public List<string> AvailableFiles;
    public bool IsReadOnly;

    public NotekeeperUiState(
        List<string> notes,
        string currentFilePath = "/notes/shift_notes.txt",
        string currentFileName = "shift_notes.txt",
        string fileContent = "",
        List<string>? availableFiles = null,
        bool isReadOnly = false)
    {
        Notes = notes;
        CurrentFilePath = currentFilePath;
        CurrentFileName = currentFileName;
        FileContent = fileContent;
        AvailableFiles = availableFiles ?? new();
        IsReadOnly = isReadOnly;
    }
}
