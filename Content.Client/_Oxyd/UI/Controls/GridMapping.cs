using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.UI;

public sealed class GridMapping : Container
{
    [ViewVariables] public Dictionary<Vector2i, GridSlot> Slots = new();
    public Vector2 gridsize = new(64,64);

    protected override void ChildAdded(Control newChild)
    {
        if (newChild is GridSlot slot)
        {
            AddSlot(slot);
        }
        base.ChildAdded(newChild);
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
        AddChild(slot);
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
        slot.SetSize = gridsize;
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
        InvalidateMeasure();
    }

    protected override void ChildRemoved(Control child)
    {
        if (child is GridSlot slot)
        {
            RemoveSlot(slot);
        }
        base.ChildRemoved(child);
    }

    public void UpdateGrid(Vector2i max)
    {
        for (var x = 0; x <= max.X; x++)
        {
            for (var y = 0; y <= max.Y; y++)
            {
                if (!Slots.TryGetValue(new Vector2i(x,y), out GridSlot? v))
                {
                    continue;
                }

                var box = new UIBox2(gridsize.X * x, y * gridsize.Y, gridsize.X * (x+1), gridsize.Y * (y+1));
                //Log.Info($"Grid slot {x},{y} is {box}");
                v.Arrange(box);
            }
        }
    }
    
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Slots.Count == 0)
            return base.MeasureOverride(availableSize);

        var maxX = Slots.Keys.Max(k => k.X);
        var maxY = Slots.Keys.Max(k => k.Y);

        var desired = new Vector2(
            (maxX + 1) * gridsize.X,
            (maxY + 1) * gridsize.Y);

        return desired;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    { 
        if(Slots.Keys.Count == 0)
            return base.ArrangeOverride(finalSize);
        Vector2i max = new Vector2i(Slots.Keys.Max(x => x.X), Slots.Keys.Max(x => x.Y));
        UpdateGrid(max);
        return finalSize;
    }
}