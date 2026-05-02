using System.Collections.Immutable;
using System.Linq;

namespace Content.Client._Oxyd.Framework;

public sealed class SyncedEntityEventArgs<T> : EntityEventArgs
{
    public required T self;
    public Dictionary<int, Func<SyncedEntityEventArgs<T>, bool>> preds { get; set; } = new();

    public void Register(int priority, Func<SyncedEntityEventArgs<T>, bool> pred)
    {
        if(preds.ContainsKey(priority))
            throw new Exception("Priority already exists for SyncedEntityEventArgs, choose another number");
        preds.Add(priority, pred);
    }

    public void Execute()
    {
        foreach (var pred in preds.OrderBy(item => item.Key))
        {
            if (pred.Value(this))
                return;
        }
    }

    public bool Execute(int priority)
    {
        return preds[priority](this);
    }
}
