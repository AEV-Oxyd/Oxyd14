using Content.Client.Gameplay;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Oxyd.UI;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class OxTagController : UIController
{
    [ViewVariables(VVAccess.ReadOnly)] Dictionary<string, List<Control>> map = new();
    
    public Action<string, Control>? Added;
    public Action<string, Control>? Removed;

    public void RegisterControl(string tag, Control parent, bool unique = false)
    {
        if (!map.ContainsKey(tag))
            map[tag] = new List<Control>();
        Log.Info("Registering control {tag} to {parent}", tag, parent);
        map[tag].Add(parent);
        Added?.Invoke(tag, parent);
    }
    
    public void UnregisterControl(string tag, Control parent)
    {
        if (!map.ContainsKey(tag))
            return;
        map[tag].Remove(parent);
        Log.Info("Unregistering control {tag} from {parent}", tag, parent);
        Removed?.Invoke(tag, parent);
    }
    
}