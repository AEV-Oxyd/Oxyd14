using Robust.Shared.GameStates;

namespace Content.Shared.CartridgeLoader.Cartridges;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NotekeeperCartridgeComponent : Component
{
    /// <summary>
    /// The path to the currently active text file.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string CurrentFilePath = "/notes/shift_notes.txt";

    /// <summary>
    /// The list of notes that got written down (kept in sync with line breaks).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> Notes = new();
}
