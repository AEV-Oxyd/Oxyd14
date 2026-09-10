using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.UI;

public sealed class GridMapping : Control
{
    public Dictionary<Vector2i, GridSlot> Slots = new();
    public Vector2 size = new(32,32);

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);
        if (newChild is GridSlot slot)
        {
            AddSlot(slot);
            UpdateGrid();
        }
    }

    public void Clear()
    {
        Slots.Clear();
        var removing = new List<Control>(8);
        foreach (var child in Children)
        {
            if (child is not GridSlot)
                continue;
            removing.Add(child);
        }
        foreach(var child in removing)
            RemoveChild(child);
    }

    public GridSlot InitSlot(Vector2i pos)
    {
        if (Slots.TryGetValue(pos, out var exist))
            return exist;
        var slot = new GridSlot();
        slot.Key = pos;
        AddSlot(slot);
        return slot;
    }

    public GridSlot InitSlot(Vector2i pos, Control add)
    {
        var slot = InitSlot(pos);
        slot.AddChild(add);
        return slot;
    }

    public GridSlot InitSlot(Vector2i pos, string name, Control add)
    {
        var slot = InitSlot(pos);
        if(slot.Children.Any(x => x.Name == name))
            return slot;
        slot.AddChild(add);
        add.Name = name;
        return slot;   
    }

    public bool GetByPosName(Vector2i pos, string name,[NotNullWhen(true)] out Control? valid)
    {
        valid = null;
        if (!Slots.TryGetValue(pos, out var slot))
            return false;
        valid = slot.Children.FirstOrDefault(t => t.Name == name);
        return valid is not null;
    }

    public bool GetByName(string name, [NotNullWhen(true)] out Control? valid)
    {
        valid = null;
        foreach (var slot in Slots.Values)
        {
            valid = slot.Children.FirstOrDefault(t => t.Name == name);
            if (valid is not null)
                return true;
        }
        return false;
    }

    public void AddSlot(GridSlot slot)
    {
        slot.owner = this;
        Slots[slot.Key] = slot;
    }

    public void RemoveSlot(GridSlot slot)
    {
        slot.owner = null;
        Slots.Remove(slot.Key);
    }

    public void CoordChange(GridSlot slot, Vector2i old)
    {
        Slots.Remove(old);
        Slots[slot.Key] = slot;
        UpdateGrid();
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        if (child is GridSlot slot)
        {
            RemoveSlot(slot);
            UpdateGrid();
        }
    }

    public void UpdateGrid()
    {
        Vector2i max = new Vector2i(Slots.Keys.Max(x => x.X), Slots.Keys.Max(x => x.Y));
        for (var x = 0; x <= max.X; x++)
        {
            for (var y = 0; y <= max.Y; y++)
            {
                if (!Slots.TryGetValue(new Vector2i(x,y), out GridSlot? v))
                {
                    v = new GridSlot();
                    v.Key = new Vector2i(x, y);
                    AddSlot(v);
                }
                v.Arrange(new UIBox2(x, y * size.Y, size.X * x, size.Y));
            }
        }
        
    }
    
}