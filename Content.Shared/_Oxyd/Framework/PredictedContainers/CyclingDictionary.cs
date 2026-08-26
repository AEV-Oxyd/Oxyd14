using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Timing;

namespace Content.Shared;

public sealed class CyclingDictionary<T>
{
    [ViewVariables] SortedDictionary<uint, T> dict = new();
    [ViewVariables] public uint limit = 25;
    
    public void Insert(uint tick, T value)
    {
        dict.Add(tick, value);
        while (dict.Count > limit)
        {
            dict.Remove(dict.Keys.First());
        }
    }

    public bool Get(uint tick, [NotNullWhen(true)] out T? value)
    {
        value = default(T);
        if(dict.TryGetValue(tick, out var val))
        {
            value = val!;
            return true;
        }
        return false;
    }

    public T this[uint index]
    {
        get => dict[index];
        set
        {
            if (dict.ContainsKey(index))
                dict[index] = value;
            else
                Insert(index, value);
        }
    }
}