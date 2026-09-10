using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.UI;

/// <summary>
/// This is a prototype for...
/// </summary>
public sealed partial class OxTag : Control
{
    // Needed cause Deparanted doesnt put as arg the old parent >:( SPCR 2026
    private Control? selfParent = null;
    public string tag
    {
        get;
        set
        {
            if (field == value)
                return;
            var c = UserInterfaceManager.GetUIController<OxTagController>();
            if (selfParent is not null)
            {
                if(field != string.Empty)
                    c.UnregisterControl(field, selfParent);
                if(value != string.Empty)
                    c.RegisterControl(value, selfParent);
            }
            field = value;
        }
    } = string.Empty;


    protected override void Parented(Control parent)
    {
        base.Parented(parent);
        selfParent = parent;
        if (tag == string.Empty)
            return;
        UserInterfaceManager.GetUIController<OxTagController>().RegisterControl(tag, selfParent);
    }

    protected override void Deparented()
    {
        base.Deparented();
        if (tag == string.Empty || selfParent is null)
            return;
        UserInterfaceManager.GetUIController<OxTagController>().UnregisterControl(tag, selfParent);
        selfParent = null;
    }
}