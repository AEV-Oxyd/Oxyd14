using System.Numerics;
using Content.Client._Oxyd.UI;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

public sealed class GridSlot : PanelContainer
{
    public GridMapping? owner = null;

    public Vector2i Key
    {
        get;
        set
        {
            var old = field;
            field = value;
            if (old == field)
                return;
            if(owner is not null)
                owner.CoordChange(this, old);
        }
    } = new();


    public GridSlot()
    {
        MinSize = new Vector2(64, 64);
        UpdateVisibility();
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);
        UpdateVisibility();
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        // ChildCount comes from the base Control class.
        Modulate = ChildCount != 0 ? Color.White : Color.Transparent;
    }
}